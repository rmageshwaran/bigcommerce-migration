# 🚀 **SignalR Centralized System - Quick Reference Guide**

## **📋 System Overview**

The centralized SignalR Event Factory eliminates scattered event creation and ensures consistency across all SignalR events in the migration system.

**Before**: 35+ manual event creation points with inconsistent properties  
**After**: 1 centralized factory with auto-populated base properties ✅

---

## **🎯 Core Components**

### **1. SignalREventFactory** 
**File**: `src/BigCommerce.Migration.Core/Services/SignalREventFactory.cs`
- **Purpose**: Single source of truth for creating all SignalR events
- **Benefits**: Auto-populates base properties, validates required fields, enforces naming consistency

### **2. SignalREventOptions**
**File**: `src/BigCommerce.Migration.Core/Models/SignalREventOptions.cs`  
- **Purpose**: Strongly-typed options classes for clean parameter passing
- **Benefits**: Type safety, optional properties, clear documentation

### **3. SignalRMessageConverter**
**File**: `src/BigCommerce.Migration.Core/Services/SignalRMessageConverter.cs`
- **Purpose**: Handles PascalCase ↔ camelCase conversion between backend/frontend
- **Benefits**: Eliminates complex transformation functions in frontend

---

## **🔧 How to Use the Factory**

### **Basic Pattern**
```csharp
// 1. Inject the factory in constructor
private readonly ISignalREventFactory _signalREventFactory;

public MyService(ISignalREventFactory signalREventFactory)
{
    _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory));
}

// 2. Use factory instead of manual creation
var progressEvent = _signalREventFactory.CreateMigrationProgress(migrationId, new MigrationProgressOptions
{
    OverallProgress = 75,
    Status = "processing",
    TotalEntities = 1000,
    ProcessedEntities = 750
    // ✅ Base properties auto-populated: Timestamp, HubMethod, IsCancelled, etc.
});
```

### **DI Registration** (Already configured ✅)
```csharp
// In ServiceCollectionExtensions.cs - Already done!
services.AddSingleton<ISignalREventFactory, SignalREventFactory>();
services.AddSingleton<ISignalRMessageConverter, SignalRMessageConverter>();
```

---

## **🎨 Factory Methods Available**

### **1. Migration Progress Events**
```csharp
var event = _signalREventFactory.CreateMigrationProgress(migrationId, new MigrationProgressOptions
{
    OverallProgress = 50.5,           // Required: Progress percentage (0-100)
    Status = "processing",            // Required: Migration status
    TotalEntities = 1000,            // Optional: Total entities to migrate
    ProcessedEntities = 500,         // Optional: Currently processed
    FailedEntities = 5,              // Optional: Failed entities
    CurrentEntityType = "products",  // Optional: Current entity being processed
    EstimatedTimeRemaining = TimeSpan.FromMinutes(30) // Optional
});
```

### **2. Entity Progress Events**
```csharp
var event = _signalREventFactory.CreateEntityProgress(migrationId, new EntityProgressOptions
{
    EntityType = "brands",           // Required: Type of entity
    Status = "completed",            // Required: Processing status  
    TotalCount = 192,               // Required: Total entities of this type
    ProcessedCount = 192,           // Required: Processed so far
    ProcessingTime = TimeSpan.FromMinutes(2) // Optional
    // ✅ SuccessCount/FailureCount calculated automatically from ProcessedCount/TotalCount
});
```

### **3. Batch Progress Events**
```csharp
var event = _signalREventFactory.CreateBatchProgress(migrationId, new BatchProgressOptions
{
    EntityType = "products",         // Required: Entity type
    BatchNumber = 3,                // Required: Current batch number
    TotalBatches = 10,              // Required: Total batches
    BatchSize = 50,                 // Required: Entities in this batch
    ProcessedCount = 50,            // Required: Processed in this batch
    FailedCount = 2,                // Required: Failed in this batch
    Status = "completed_with_errors", // Required: Batch status
    ProcessingTime = TimeSpan.FromSeconds(45) // Optional
});
```

### **4. Error Progress Events**
```csharp
var event = _signalREventFactory.CreateErrorProgress(migrationId, new ErrorProgressOptions
{
    ErrorMessage = "Rate limit exceeded", // Required: Human-readable error
    EntityType = "products",             // Optional: Entity that caused error
    EntityId = "prod_123",              // Optional: Specific entity ID
    Severity = "Warning",               // Optional: Error severity
    Exception = ex.ToString(),          // Optional: Full exception details
    StackTrace = ex.StackTrace         // Optional: Stack trace for debugging
});
```

### **5. Status Progress Events**
```csharp
var event = _signalREventFactory.CreateStatusProgress(migrationId, new StatusProgressOptions
{
    Status = "completed",               // Required: Migration status
    Message = "Migration completed successfully", // Optional: Human-readable message
    Data = new {                       // Optional: Additional metadata
        TotalEntities = 1000,
        SuccessfulEntities = 995,
        FailedEntities = 5
    }
});
```

### **6. Sub-Batch Events** (For parallel processing)
```csharp
// Sub-batch started
var startedEvent = _signalREventFactory.CreateSubBatchStarted(migrationId, new SubBatchStartedOptions
{
    ParentBatchNumber = 2,             // Required: Parent batch number
    SubBatchNumber = 1,                // Required: Sub-batch number
    TotalSubBatches = 10,              // Required: Total sub-batches in parent
    EntityType = "brands",             // Required: Entity type
    EntitiesInBatch = 5,              // Required: Entities in this sub-batch
    MaxConcurrency = 3                // Optional: Concurrency limit
});

// Sub-batch completed  
var completedEvent = _signalREventFactory.CreateSubBatchCompleted(migrationId, new SubBatchCompletedOptions
{
    ParentBatchNumber = 2,             // Required: Parent batch number
    SubBatchNumber = 1,                // Required: Sub-batch number
    TotalSubBatches = 10,              // Required: Total sub-batches
    EntityType = "brands",             // Required: Entity type
    SuccessfulEntities = 4,            // Required: Successful in this sub-batch
    FailedEntities = 1,                // Required: Failed in this sub-batch
    TotalEntities = 5,                 // Required: Total in this sub-batch
    ProcessingTime = TimeSpan.FromSeconds(12), // Optional: Processing time
    CumulativeSuccessfulEntities = 150, // Optional: Overall successful so far
    CumulativeFailedEntities = 10,     // Optional: Overall failed so far
    TotalMigrationEntities = 192       // Optional: Overall migration total
});
```

---

## **✅ Migration Conversion Patterns**

### **Converting Manual Event Creation**

#### **❌ BEFORE: Manual Creation**
```csharp
var progressEvent = new MigrationProgressEvent
{
    MigrationId = migrationId,
    OverallProgress = progress.OverallProgressPercentage,
    Status = progress.Status,
    TotalEntities = progress.TotalEntities,
    ProcessedEntities = progress.ProcessedEntities,
    FailedEntities = progress.FailedEntities,
    CurrentEntityType = progress.CurrentEntity,
    EstimatedTimeRemaining = progress.EstimatedTimeRemaining,
    Timestamp = DateTime.UtcNow,       // ❌ Manual, inconsistent
    IsCancelled = false,               // ❌ Often forgotten
    HubMethod = "MigrationProgressUpdated" // ❌ Hardcoded, inconsistent
};
```

#### **✅ AFTER: Centralized Factory**
```csharp
var progressEvent = _signalREventFactory.CreateMigrationProgress(migrationId, new MigrationProgressOptions
{
    OverallProgress = progress.OverallProgressPercentage,
    Status = progress.Status,
    TotalEntities = progress.TotalEntities,
    ProcessedEntities = progress.ProcessedEntities,
    FailedEntities = progress.FailedEntities,
    CurrentEntityType = progress.CurrentEntity,
    EstimatedTimeRemaining = progress.EstimatedTimeRemaining
    // ✅ Base properties auto-populated: Timestamp, IsCancelled, HubMethod
    // ✅ Validation built-in: Required fields checked
    // ✅ Consistent naming: HubMethod follows convention
});
```

### **Adding Factory to Existing Services**

#### **Step 1: Add Constructor Parameter**
```csharp
public class MyService
{
    private readonly ISignalREventFactory _signalREventFactory;
    
    public MyService(
        // ... existing parameters
        ISignalREventFactory signalREventFactory) // ✅ Add this
    {
        // ... existing assignments
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory));
    }
}
```

#### **Step 2: Add Using Statement**
```csharp
using BigCommerce.Migration.Core.Services; // ✅ Add this for ISignalREventFactory
```

#### **Step 3: Replace Manual Event Creation**
Find and replace patterns like:
```csharp
// Find: new MigrationProgressEvent { ... }
// Replace with: _signalREventFactory.CreateMigrationProgress(migrationId, new MigrationProgressOptions { ... })
```

---

## **🧪 Testing Patterns**

### **Mock Setup for Tests**
```csharp
[Test]
public void MyTest()
{
    // Arrange
    var mockSignalREventFactory = new Mock<ISignalREventFactory>();
    var expectedEvent = new MigrationProgressEvent(); // Create expected event
    
    mockSignalREventFactory
        .Setup(f => f.CreateMigrationProgress(It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
        .Returns(expectedEvent);
    
    var service = new MyService(mockSignalREventFactory.Object);
    
    // Act & Assert
    // ... test code
}
```

### **Verifying Factory Usage**
```csharp
// Verify factory was called with expected parameters
mockSignalREventFactory.Verify(f => f.CreateMigrationProgress(
    "test-migration-id",
    It.Is<MigrationProgressOptions>(opt => 
        opt.OverallProgress == 50 && 
        opt.Status == "processing")), 
    Times.Once);
```

---

## **🚨 Common Gotchas & Solutions**

### **1. Missing Constructor Parameter**
**Error**: `CS7036: There is no argument given that corresponds to the required parameter 'signalREventFactory'`  
**Solution**: Add `ISignalREventFactory signalREventFactory` to constructor and pass it in calls

### **2. Property Name Mismatch**
**Error**: `CS0117: 'StatusProgressOptions' does not contain a definition for 'Metadata'`  
**Solution**: Use correct property name (`Data` instead of `Metadata`)

### **3. Missing Using Statement** 
**Error**: `CS0246: The type or namespace name 'ISignalREventFactory' could not be found`  
**Solution**: Add `using BigCommerce.Migration.Core.Services;`

### **4. Type Ambiguity**
**Error**: `CS0104: 'ValidationResult' is an ambiguous reference`  
**Solution**: Use fully qualified name: `BigCommerce.Migration.Core.Interfaces.ValidationResult`

---

## **📊 Event Type Quick Reference**

| Event Type | Factory Method | Key Properties | Use Case |
|------------|----------------|----------------|----------|
| **MigrationProgressEvent** | `CreateMigrationProgress()` | OverallProgress, Status, TotalEntities | Overall migration progress |
| **EntityProgressEvent** | `CreateEntityProgress()` | EntityType, TotalCount, ProcessedCount | Per-entity type progress |
| **BatchProgressEvent** | `CreateBatchProgress()` | BatchNumber, TotalBatches, ProcessedCount | Batch processing progress |
| **ErrorProgressEvent** | `CreateErrorProgress()` | ErrorMessage, EntityType, Severity | Error reporting |
| **StatusProgressEvent** | `CreateStatusProgress()` | Status, Message, Data | Status changes |
| **SubBatchStartedEvent** | `CreateSubBatchStarted()` | ParentBatchNumber, SubBatchNumber | Sub-batch parallel start |
| **SubBatchCompletedEvent** | `CreateSubBatchCompleted()` | SuccessfulEntities, FailedEntities | Sub-batch parallel completion |

---

## **🎯 Next Steps When You Return**

1. **Start with Task 1**: Fix test project constructor signatures
2. **Use the search commands** in the task list to find remaining manual events
3. **Follow the conversion patterns** shown above
4. **Test each component** after conversion
5. **Update documentation** as you complete each task

**Remember**: The core production system is **100% centralized** ✅ - remaining work is cleanup and optimization!

---

**Quick Start Command**: `grep -r "new.*ProgressEvent" src/ --include="*.cs" | grep -v "SignalREventFactory.cs"` 