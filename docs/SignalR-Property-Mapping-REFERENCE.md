# 🎯 SignalR Property Mapping - Quick Reference

**Purpose:** Map frontend UI properties to backend SignalR event properties  
**Status:** Analysis Complete - Implementation Required  
**Last Updated:** {Current Date}  

---

## 📊 **DASHBOARD SECTIONS MAPPING**

### **1. Overall Migration Progress Section**

| Frontend Property | Backend Property | Current Status | Notes |
|-------------------|------------------|----------------|-------|
| `overallProgressPercentage` | ✅ `OverallProgress` | ✅ **FIXED** | **COMPLETED:** Now entity-based (100/192) not batch-based (2/4) |
| `totalEntities` | ✅ `TotalEntities` | ✅ **FIXED** | **COMPLETED:** Now fixed at 192, not dynamic ProcessedEntities+FailedEntities |
| `processedEntities` | ✅ `ProcessedEntities` | ✅ Working | Direct mapping |
| `successfulEntities` | ✅ `SuccessfulEntities` | ✅ **FIXED** | **COMPLETED:** Backend now calculates ProcessedEntities - FailedEntities |
| `elapsedTime` | ✅ `ElapsedTime` | ✅ **FIXED** | **COMPLETED:** Backend now tracks StartTime and calculates ElapsedTime |
| `entitiesPerSecond` | ✅ `EntitiesPerSecond` | ✅ **FIXED** | **COMPLETED:** Backend now calculates processing speed (ProcessedEntities / ElapsedTime.TotalSeconds) |
| `estimatedTimeRemaining` | ✅ `EstimatedTimeRemaining` | ✅ **FIXED** | **COMPLETED:** Backend now auto-calculates based on processing speed and remaining entities |

### **2. Current Processing Status Section**

| Frontend Property | Backend Property | Current Status | Notes |
|-------------------|------------------|----------------|-------|
| `currentEntity` | ✅ `CurrentEntityType` | ✅ Working | Direct mapping |
| `currentBatchNumber` | ✅ `CurrentBatchNumber` | ✅ **FIXED** | **COMPLETED:** Backend now supports real batch tracking |
| `currentActivity` | ✅ `CurrentActivity` | ✅ **FIXED** | **COMPLETED:** Backend now supports processing phase tracking |
| `currentBatch.batchSize` | ✅ `CurrentBatch.BatchSize` | ✅ **FIXED** | **COMPLETED:** CurrentBatchDetails structure implemented |
| `currentBatch.processedInBatch` | ✅ `CurrentBatch.ProcessedInBatch` | ✅ **FIXED** | **COMPLETED:** CurrentBatchDetails structure implemented |
| `currentBatch.batchProgressPercentage` | ✅ `CurrentBatch.BatchProgressPercentage` | ✅ **FIXED** | **COMPLETED:** CurrentBatchDetails structure implemented |

### **3. Batch Summary Section**

| Frontend Property | Backend Property | Current Status | Notes |
|-------------------|------------------|----------------|-------|
| `totalBatches` | ❌ Missing | ❌ Broken | **TASK:** Add BatchSummary accumulation |
| `completedBatches` | ❌ Missing | ❌ Broken | **TASK:** Add BatchSummary accumulation |
| `remainingBatches` | ❌ Missing | ❌ Broken | **TASK:** Add BatchSummary accumulation |
| `batchCompletionPercentage` | ❌ Missing | ❌ Broken | **TASK:** Calculate completedBatches / totalBatches * 100 |

### **4. Remaining Work Section**

| Frontend Property | Backend Property | Current Status | Notes |
|-------------------|------------------|----------------|-------|
| `remainingEntities` | ⚠️ Calculated | ⚠️ Frontend calc | **TASK:** Send from backend (TotalEntities - ProcessedEntities) |
| `remainingBatches` | ❌ Missing | ❌ Broken | **TASK:** Add to BatchSummary |
| `estimatedTimeRemaining` | ⚠️ `EstimatedTimeRemaining` | ❌ Broken | **TASK:** Fix calculation |

### **5. Completion Estimate Section**

| Frontend Property | Backend Property | Current Status | Notes |
|-------------------|------------------|----------------|-------|
| `entitiesPerSecond` | ❌ Missing | ❌ Broken | **TASK:** Calculate from ElapsedTime + ProcessedEntities |
| `expectedCompletion` | ⚠️ Calculated | ⚠️ Frontend calc | **TASK:** Calculate Date.now() + EstimatedTimeRemaining |

---

## 🏗️ **BACKEND STRUCTURES NEEDED**

### **Enhanced MigrationProgressEvent**
```csharp
public class MigrationProgressEvent : ProgressEvent
{
    // ✅ Existing Properties (Working)
    public double OverallProgress { get; set; }
    public int TotalEntities { get; set; }
    public int ProcessedEntities { get; set; }
    public int FailedEntities { get; set; }
    public string? CurrentEntityType { get; set; }
    
    // 🎯 NEW: Time Tracking (Task: backend_missing_properties)
    public DateTime StartTime { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public double EntitiesPerSecond { get; set; }
    
    // 🎯 NEW: Calculated Properties (Task: backend_success_entities)
    public int SuccessfulEntities { get; set; } // ProcessedEntities - FailedEntities
    
    // 🎯 NEW: Current Batch Details (Task: backend_batch_details)
    public CurrentBatchDetails CurrentBatch { get; set; }
    public int CurrentBatchNumber { get; set; }
    public string CurrentActivity { get; set; } // "Processing", "Fetching", "Transforming", etc.
    
    // 🎯 NEW: Batch Summary (Task: backend_batch_summary)
    public BatchSummary BatchSummary { get; set; }
    
    // 🎯 NEW: Performance Metrics (Task: backend_performance_metrics)
    public PerformanceMetrics Performance { get; set; }
    
    // 🎯 FIXED: Estimated Time Remaining (Task: backend_estimated_time_fix)
    public TimeSpan? EstimatedTimeRemaining { get; set; } // Calculate properly, not TimeSpan.Zero
}
```

### **New Supporting Structures**
```csharp
// Task: backend_batch_details
public class CurrentBatchDetails
{
    public int BatchNumber { get; set; }
    public int BatchSize { get; set; }
    public int ProcessedInBatch { get; set; }
    public double BatchProgressPercentage { get; set; }
    public double BatchProcessingSpeed { get; set; }
    public TimeSpan BatchElapsedTime { get; set; }
    public TimeSpan EstimatedBatchTimeRemaining { get; set; }
}

// Task: backend_batch_summary
public class BatchSummary
{
    public int TotalBatches { get; set; }
    public int CompletedBatches { get; set; }
    public int ProcessingBatches { get; set; }
    public int RemainingBatches { get; set; }
    public double BatchCompletionPercentage { get; set; }
    public Dictionary<string, EntityBatchProgress> EntityBatches { get; set; }
}

// Task: backend_performance_metrics
public class PerformanceMetrics
{
    public double CurrentProcessingSpeed { get; set; } // entities/second
    public double AverageProcessingSpeed { get; set; }
    public double PeakProcessingSpeed { get; set; }
    public string PerformanceTrend { get; set; } // "improving", "stable", "declining"
    public DateTime LastCalculation { get; set; }
}
```

---

## 🎨 **FRONTEND CLEANUP REQUIRED**

### **Remove These Frontend Calculations**
```typescript
// ❌ REMOVE: Frontend calculating backend data
// File: src/BigCommerce.Migration.Dashboard/src/services/signalRService.ts
// Function: enrichMigrationProgressEvent()

// Remove these calculations:
const successfulEntities = processedEntities - failedEntities; // Use backend
const entitiesPerSecond = elapsedTime > 0 ? processedEntities / (elapsedTime / 1000) : 0; // Use backend
currentBatchNumber: Math.ceil(processedEntities / 50) || 1, // Use backend
batchProgressPercentage: (overallProgress % 10) * 10, // Use backend
totalBatches: Math.ceil(totalEntities / 50) || 1, // Use backend
```

### **Update These Frontend Interfaces**
```typescript
// ✅ UPDATE: Match backend properties
// File: src/BigCommerce.Migration.Dashboard/src/hooks/useDetailedMigrationProgress.ts

export interface DetailedMigrationProgress {
    // Add missing properties to match backend
    startTime: Date;
    elapsedTime: number;
    entitiesPerSecond: number;
    currentBatchNumber: number;
    currentActivity: string;
    // ... other new properties
}
```

---

## 📋 **IMPLEMENTATION CHECKLIST**

### **Phase 1: Backend Properties** ⏳
- [ ] `backend_missing_properties` - Add StartTime, ElapsedTime, EntitiesPerSecond
- [ ] `backend_success_entities` - Add SuccessfulEntities calculation
- [ ] `backend_estimated_time_fix` - Fix EstimatedTimeRemaining calculation

### **Phase 2: Backend Structures** ⏳
- [ ] `backend_batch_details` - Add CurrentBatchDetails structure
- [ ] `backend_batch_summary` - Add BatchSummary accumulation
- [ ] `backend_performance_metrics` - Add PerformanceMetrics tracking

### **Phase 3: Integration** ⏳
- [ ] `progress_tracker_accumulation` - Update ProgressTracker for accumulation
- [ ] `signalr_event_factory_update` - Update factory for new properties

### **Phase 4: Frontend** ⏳
- [ ] `frontend_remove_calculations` - Remove frontend calculations
- [ ] `frontend_property_mapping` - Update interfaces

### **Phase 5: Testing** ⏳
- [ ] `realtime_update_testing` - End-to-end validation

---

## 🔗 **RELATED DOCUMENTATION**
- [Task List](./SignalR-RealTime-Dashboard-TASK-LIST.md) - Detailed task breakdown
- [SignalR Centralization Status](./SignalR-Centralization-QUICK-REFERENCE.md) - Overall project status

---

## **🚨 CRITICAL ISSUE DISCOVERED**

**Sub-batch events do NOT update Overall Migration Progress section**

### **Root Cause:** Property Name Mismatches
- Frontend expects: `overallProgressPercentage`, `processedEntities`, `totalEntities`
- Sub-batch events have: `cumulativeSuccessfulEntities`, `progressPercentage`, `totalMigrationEntities`
- Result: Sub-batch processing doesn't update main progress bar

### **Impact:** 
- Progress bar only updates on final completion (1 event vs 40 sub-batch events)
- Missing **40x more granular progress updates**
- Dashboard appears "stuck" during active migration

---

**🎯 PRIORITY CHANGE:** Start with **Phase 2.5 Sub-Batch Real-Time UI Updates** first  
**Quick Start:** Begin with `subbatch_hook_subscriptions` → `subbatch_overall_progress_mapping` → `subbatch_state_management` 