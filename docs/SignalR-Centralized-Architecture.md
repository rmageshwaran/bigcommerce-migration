# 🎯 **SignalR Centralized Architecture Guide**

## **Overview**

This document describes the **centralized SignalR Event Factory and Message Converter system** that solves consistency issues across the entire migration platform.

## **❌ Problems Solved**

### **Before: Scattered Event Creation (21+ Locations)**
```csharp
// ❌ SCATTERED: Manual event creation in ProcessParallelBatchesActivity.cs
var completedEvent = new SubBatchCompletedEvent
{
    MigrationId = migrationId,
    ParentBatchNumber = batchNumber,
    SubBatchNumber = subBatchIndex + 1,
    // ... 15+ properties manually set
    Timestamp = DateTime.UtcNow,  // ❌ Inconsistent
    IsCancelled = false,          // ❌ Often forgotten
    HubMethod = "SubBatchCompleted", // ❌ Inconsistent naming
};

// ❌ SCATTERED: Manual event creation in ParallelProgressAggregator.cs  
var progressEvent = new MigrationProgressEvent
{
    MigrationId = _migrationId,
    CurrentEntityType = _entityType,
    // ... missing base properties
    // ❌ No validation, no consistency checks
};

// ❌ FRONTEND: Complex transformation functions (8 different ones)
private transformSubBatchCompletedEvent(backendEvent: any): any {
    return {
        migrationId: backendEvent.MigrationId || backendEvent.migrationId, // ❌ Fallback logic
        parentBatchNumber: backendEvent.ParentBatchNumber || backendEvent.parentBatchNumber,
        // ... 20+ lines of property mapping
    };
}
```

### **After: Centralized Event Factory**
```csharp
// ✅ CENTRALIZED: Single factory for all events
var completedEvent = _signalREventFactory.CreateSubBatchCompleted(migrationId, new SubBatchCompletedOptions
{
    ParentBatchNumber = batchNumber,
    SubBatchNumber = subBatchIndex + 1,
    SuccessfulEntities = subBatchResult.SuccessfulEntities,
    FailedEntities = subBatchResult.FailedEntities,
    // ✅ Base properties auto-populated: Timestamp, IsCancelled, HubMethod
    // ✅ Validation built-in
    // ✅ Consistent naming enforced
});

// ✅ FRONTEND: No transformation needed - automatic camelCase conversion
this.connection.on('SubBatchCompleted', (eventData: any) => {
    // ✅ eventData already has consistent camelCase properties
    // ✅ No manual transformation required
    this.notifyListeners('subBatchCompleted', eventData);
});
```

## **🏗️ Architecture Components**

### **1. SignalR Event Factory (`ISignalREventFactory`)**
**Single source of truth for all SignalR event creation**

```csharp
public interface ISignalREventFactory
{
    // ✅ Strongly typed options for each event type
    MigrationProgressEvent CreateMigrationProgress(string migrationId, MigrationProgressOptions options);
    BatchProgressEvent CreateBatchProgress(string migrationId, BatchProgressOptions options);
    EntityProgressEvent CreateEntityProgress(string migrationId, EntityProgressOptions options);
    ErrorProgressEvent CreateErrorProgress(string migrationId, ErrorProgressOptions options);
    StatusProgressEvent CreateStatusProgress(string migrationId, StatusProgressOptions options);
    SubBatchStartedEvent CreateSubBatchStarted(string migrationId, SubBatchStartedOptions options);
    SubBatchCompletedEvent CreateSubBatchCompleted(string migrationId, SubBatchCompletedOptions options);
    SubBatchMigrationProgressEvent CreateSubBatchProgress(string migrationId, SubBatchProgressOptions options);
}
```

**✅ Features:**
- **Auto-populates base properties**: `Timestamp`, `IsCancelled`, `HubMethod`
- **Built-in validation**: Required properties enforced
- **Consistent naming**: Standardized HubMethod names
- **Type safety**: Strongly-typed options classes

### **2. SignalR Message Converter (`ISignalRMessageConverter`)**
**Handles PascalCase ↔ camelCase conversion automatically**

```csharp
public interface ISignalRMessageConverter
{
    // ✅ Automatic PascalCase → camelCase conversion for frontend
    string ConvertToFrontendJson(ProgressEvent progressEvent);
    object ConvertToFrontendObject(ProgressEvent progressEvent);
    object CreateSignalRMessage(ProgressEvent progressEvent);
}
```

**✅ Features:**
- **Automatic case conversion**: Backend PascalCase → Frontend camelCase
- **JSON serialization**: Optimized for frontend consumption
- **No transformation functions needed**: Eliminates complex frontend mapping

### **3. Consistency Contract (`SignalRConsistencyContract`)**
**Enforces standardization across all events**

```csharp
public static class SignalRConsistencyContract
{
    // ✅ Required properties for ALL events
    public static readonly string[] RequiredBaseProperties = 
    {
        "migrationId", "eventType", "timestamp", "hubMethod"
    };

    // ✅ Standardized HubMethod names
    public static readonly Dictionary<string, string> StandardizedHubMethods = new()
    {
        ["progress"] = "MigrationProgressUpdated",
        ["batch"] = "BatchProgressUpdated", 
        ["entity"] = "EntityProgressUpdated",
        ["error"] = "ErrorOccurred",
        ["status"] = "MigrationStatusChanged",
        ["subbatch-started"] = "SubBatchStarted",
        ["subbatch-completed"] = "SubBatchCompleted", 
        ["subbatch-progress"] = "SubBatchMigrationProgress"
    };

    // ✅ Built-in validation
    public static ValidationResult ValidateEvent(ProgressEvent progressEvent);
}
```

## **📋 Migration Guide**

### **Step 1: Update Service Registration**

The centralized services are **already registered** in DI:

```csharp
// ✅ Already added to ServiceCollectionExtensions.cs
services.AddSingleton<ISignalREventFactory, SignalREventFactory>();
services.AddSingleton<ISignalRMessageConverter, SignalRMessageConverter>();
```

### **Step 2: Replace Manual Event Creation**

**Before:**
```csharp
// ❌ Manual creation (scattered across 21+ files)
var progressEvent = new MigrationProgressEvent
{
    MigrationId = migrationId,
    OverallProgress = progress,
    Status = "running",
    TotalEntities = total,
    ProcessedEntities = processed,
    FailedEntities = failed,
    CurrentEntityType = entityType,
    Timestamp = DateTime.UtcNow,        // ❌ Manual
    IsCancelled = false,                // ❌ Often forgotten
    EventType = "progress",             // ❌ Manual
    HubMethod = "MigrationProgressUpdated" // ❌ Inconsistent
};
```

**After:**
```csharp
// ✅ Centralized factory (inject ISignalREventFactory)
private readonly ISignalREventFactory _signalREventFactory;

var progressEvent = _signalREventFactory.CreateMigrationProgress(migrationId, new MigrationProgressOptions
{
    OverallProgress = progress,
    Status = "running", // Optional - defaults to "running"
    TotalEntities = total,
    ProcessedEntities = processed,
    FailedEntities = failed,
    CurrentEntityType = entityType
    // ✅ Base properties auto-populated
    // ✅ Validation built-in
    // ✅ Consistent naming enforced
});
```

### **Step 3: Simplify Frontend Event Handlers**

**Before:**
```typescript
// ❌ Complex transformation functions (8 different ones)
private transformSubBatchCompletedEvent(backendEvent: any): any {
    return {
        migrationId: backendEvent.MigrationId || backendEvent.migrationId,
        parentBatchNumber: backendEvent.ParentBatchNumber || backendEvent.parentBatchNumber,
        subBatchNumber: backendEvent.SubBatchNumber || backendEvent.subBatchNumber,
        // ... 20+ lines of manual property mapping
    };
}

this.connection.on('SubBatchCompleted', (backendEvent: any) => {
    const transformedEvent = this.transformSubBatchCompletedEvent(backendEvent);
    this.notifyListeners('subBatchCompleted', transformedEvent);
});
```

**After:**
```typescript
// ✅ No transformation needed - automatic camelCase conversion
this.connection.on('SubBatchCompleted', (eventData: any) => {
    // ✅ eventData already has consistent camelCase properties:
    // migrationId, parentBatchNumber, subBatchNumber, etc.
    this.notifyListeners('subBatchCompleted', eventData);
});
```

## **🔧 Usage Examples**

### **Example 1: Migration Progress Update**

```csharp
// Inject the factory
private readonly ISignalREventFactory _signalREventFactory;
private readonly IProgressEventPublisher _progressEventPublisher;

// Create and publish progress update
var progressEvent = _signalREventFactory.CreateMigrationProgress(migrationId, new MigrationProgressOptions
{
    OverallProgress = 65.5,
    Status = "running",
    TotalEntities = 1000,
    ProcessedEntities = 655,
    FailedEntities = 10,
    CurrentEntityType = "products"
});

await _progressEventPublisher.PublishMigrationProgressAsync(progressEvent);
```

### **Example 2: Error Event**

```csharp
var errorEvent = _signalREventFactory.CreateErrorProgress(migrationId, new ErrorProgressOptions
{
    ErrorMessage = "Failed to create product variant",
    EntityType = "product",
    EntityId = "12345",
    BatchNumber = 5,
    Severity = "Error",
    Exception = ex.ToString()
});

await _progressEventPublisher.PublishErrorAsync(errorEvent);
```

### **Example 3: Sub-Batch Completion**

```csharp
var completedEvent = _signalREventFactory.CreateSubBatchCompleted(migrationId, new SubBatchCompletedOptions
{
    ParentBatchNumber = 3,
    SubBatchNumber = 7,
    TotalSubBatches = 10,
    SuccessfulEntities = 48,
    FailedEntities = 2,
    TotalEntities = 50,
    EntityType = "brands",
    ProcessingTime = TimeSpan.FromSeconds(12.5),
    CumulativeSuccessfulEntities = 300,
    CumulativeFailedEntities = 15,
    TotalMigrationEntities = 500,
    ProgressPercentage = 63.0
});

await _progressEventPublisher.PublishSubBatchCompletedAsync(completedEvent);
```

## **✅ Benefits**

### **For Backend Developers:**
- **Single creation point**: No more scattered event creation
- **Built-in validation**: Required properties enforced
- **Type safety**: Strongly-typed options classes
- **Consistent naming**: Standardized across all events
- **Auto-population**: Base properties filled automatically

### **For Frontend Developers:**
- **No transformation functions**: Automatic camelCase conversion
- **Consistent contracts**: Same structure for all events
- **Type safety**: TypeScript interfaces match backend exactly
- **Simplified handlers**: Just receive and use events directly

### **For System Architecture:**
- **Maintainability**: Single place to update event structure
- **Consistency**: Enforced contracts across frontend/backend
- **Debugging**: Centralized logging and validation
- **Performance**: Optimized JSON serialization

## **🧪 Testing**

The centralized system includes comprehensive testing:

```csharp
// Factory validation tests
[Fact]
public void CreateMigrationProgress_WithValidOptions_CreatesValidEvent()
{
    var options = new MigrationProgressOptions { /* ... */ };
    var progressEvent = _factory.CreateMigrationProgress("test-id", options);
    
    Assert.Equal("test-id", progressEvent.MigrationId);
    Assert.True(progressEvent.Timestamp > DateTime.UtcNow.AddMinutes(-1));
    Assert.Equal("MigrationProgressUpdated", progressEvent.HubMethod);
}

// Consistency contract tests
[Fact] 
public void ValidateEvent_WithValidEvent_ReturnsSuccess()
{
    var result = SignalRConsistencyContract.ValidateEvent(progressEvent);
    Assert.True(result.IsValid);
}

// Message converter tests
[Fact]
public void ConvertToFrontendObject_WithPascalCaseEvent_ReturnsClientCaseObject()
{
    var frontendObject = _converter.ConvertToFrontendObject(progressEvent);
    // Verifies PascalCase → camelCase conversion
}
```

## **📈 Impact**

### **Before → After Comparison:**

| Aspect | Before | After |
|--------|--------|-------|
| **Event Creation Points** | 21+ scattered locations | 1 centralized factory |
| **Property Consistency** | Manual, error-prone | Auto-validated |
| **Naming Convention** | Inconsistent | Standardized |
| **Frontend Mapping** | 8 transformation functions | Automatic conversion |
| **Base Properties** | Often forgotten | Auto-populated |
| **Type Safety** | Weak | Strong typing |
| **Maintainability** | High complexity | Single source of truth |
| **Debugging** | Scattered logs | Centralized validation |

This centralized architecture **eliminates inconsistencies**, **reduces maintenance burden**, and **ensures perfect frontend-backend compatibility** across the entire SignalR messaging system! 🚀 