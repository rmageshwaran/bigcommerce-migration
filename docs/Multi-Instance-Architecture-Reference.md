# Multi-Instance Architecture Reference

## 📋 Overview

This document provides a comprehensive reference for understanding how the BigCommerce Migration System handles **multi-instance scaling** scenarios when Azure Functions auto-scales to multiple instances.

## 🎯 Current Multi-Instance Capabilities

### **✅ Features That Work Perfectly (No Changes Needed)**

#### **1. Migration Cancellation** 
- **Status**: ✅ **FULLY IMPLEMENTED**
- **Architecture**: Storage-based coordination via Azure Table Storage
- **Performance**: Sub-minute cancellation detection across all instances
- **Reference**: [Multi-Instance Cancellation Feature Reference](./Feature-Reference-Multi-Instance-Cancellation.md)

#### **2. Progress Tracking**
- **Status**: ✅ **FULLY IMPLEMENTED** 
- **Architecture**: Azure Table Storage for migration state
- **Performance**: Real-time progress updates across all instances

#### **3. Entity ID Mapping**
- **Status**: ✅ **FULLY IMPLEMENTED**
- **Architecture**: Azure Table Storage for source→destination mappings
- **Performance**: Consistent entity relationships across all instances

#### **4. Error Tracking & Logging**
- **Status**: ✅ **FULLY IMPLEMENTED**
- **Architecture**: OpenSearch for centralized logging
- **Performance**: Complete error visibility across all instances

### **⚠️ Features That Need Enhancement (When Scaling Occurs)**

#### **1. Rate Limiting**
- **Current Status**: ❌ **Instance-local** (in-memory coordination)
- **Multi-Instance Issue**: Rate limit violations when 3+ instances active
- **Solution**: Redis-based distributed rate limiting
- **Reference**: [Distributed Rate Limiting Enhancement](./Feature-Enhancement-Distributed-Rate-Limiting.md)

---

## 🏗️ Architecture Patterns

### **✅ Proven Pattern: Shared Storage Coordination**

**Used By**: Cancellation, Progress Tracking, Entity Mapping, Error Logging

```csharp
// Pattern: All instances read/write shared storage
public class SharedStateService
{
    private readonly TableClient _tableClient; // Azure Table Storage
    
    public async Task<T> GetSharedStateAsync(string key)
    {
        // ALL instances read from SAME storage
        return await _tableClient.GetEntityAsync<T>("partition", key);
    }
    
    public async Task SetSharedStateAsync(string key, T value)  
    {
        // ALL instances write to SAME storage
        await _tableClient.UpsertEntityAsync(CreateEntity("partition", key, value));
    }
}

// Result: Perfect coordination across unlimited instances ✅
```

### **❌ Anti-Pattern: Instance-Local State**

**Used By**: Current Rate Limiting (needs enhancement)

```csharp
// Anti-Pattern: Each instance has separate state
public class InstanceLocalService
{
    private readonly ConcurrentDictionary<string, T> _localState; // In-memory
    
    public T GetLocalState(string key)
    {
        // Each instance has DIFFERENT state
        return _localState.GetOrAdd(key, defaultValue);
    }
}

// Result: Coordination problems with multiple instances ❌
```

---

## 📊 Scaling Scenarios

### **Scenario 1: Single Instance (Current Typical Usage)**
```bash
Azure Function App
└── Instance 1
    ├── Migration A (Categories) ✅
    ├── Migration B (Products) ✅  
    └── Migration C (Variants) ✅
    
Rate Limiting: ✅ Perfect (in-memory works fine)
Cancellation: ✅ Perfect (storage-based)
Progress: ✅ Perfect (storage-based)
```

### **Scenario 2: Multi-Instance (High Load Scaling)**
```bash
Azure Function App (Auto-Scaled)
├── Instance 1: Migration A (Categories) ✅
├── Instance 2: Migration B (Products Batch 1-50) ✅
└── Instance 3: Migration B (Products Batch 51-100) ✅

Rate Limiting: ❌ Coordination issue (needs Redis)
  Instance 1: Thinks 4/12 requests used
  Instance 2: Thinks 4/12 requests used  
  Instance 3: Thinks 4/12 requests used
  Reality: 12/12 = Rate limit violation!

Cancellation: ✅ Perfect coordination
  User cancels Migration B
  Instance 2: Reads Azure Table → Stops ✅
  Instance 3: Reads Azure Table → Stops ✅

Progress: ✅ Perfect coordination  
  All instances update shared Azure Table Storage

Error Logging: ✅ Perfect coordination
  All instances log to shared OpenSearch cluster
```

---

## 🔧 Implementation Guidelines

### **When Building New Features (Multi-Instance Ready)**

#### **✅ DO: Use Shared Storage Pattern**
```csharp
// New feature example: Task Queue Management
public class TaskQueueService
{
    private readonly TableClient _tableClient;
    
    public async Task<QueueItem> GetNextTaskAsync()
    {
        // Read from shared storage - works across all instances
        return await _tableClient.QueryAsync<QueueItem>()
            .Where(x => x.Status == "Pending")
            .FirstOrDefaultAsync();
    }
    
    public async Task MarkTaskCompletedAsync(string taskId)
    {
        // Write to shared storage - visible to all instances
        await _tableClient.UpdateEntityAsync(taskId, "Completed");
    }
}
```

#### **❌ DON'T: Use Instance-Local State**
```csharp
// Anti-pattern example
public class BadTaskQueueService
{
    private readonly Queue<TaskItem> _localQueue; // ❌ Instance-specific
    
    public TaskItem GetNextTask()
    {
        // Only this instance sees these tasks
        return _localQueue.Dequeue();
    }
}
```

### **Design Checklist for Multi-Instance Features**

#### **State Management**
- [ ] ✅ Uses shared storage (Azure Table Storage, OpenSearch, etc.)
- [ ] ❌ No instance-local state for shared data
- [ ] ✅ Idempotent operations (safe to retry)
- [ ] ✅ Atomic operations where needed

#### **Coordination**
- [ ] ✅ All instances can read same data
- [ ] ✅ All instances can write shared data
- [ ] ✅ No race conditions between instances
- [ ] ✅ Proper conflict resolution

#### **Performance**
- [ ] ✅ Storage queries optimized (<100ms typical)
- [ ] ✅ Minimal network overhead
- [ ] ✅ Graceful degradation if storage unavailable

---

## 📈 Monitoring Multi-Instance Behavior

### **Key Metrics to Track**

#### **1. Instance Scaling Detection**
```bash
# Azure CLI commands to monitor scaling
az monitor metrics list \
  --resource-group prod-rg \
  --resource-type "Microsoft.Web/sites" \
  --resource bigcommerce-migration-functions \
  --metric "FunctionExecutionCount"

# Watch for multiple instance IDs
az functionapp list-instances \
  --name bigcommerce-migration-functions \
  --resource-group prod-rg
```

#### **2. Feature Coordination Health**
```kusto
// Application Insights KQL - Multi-instance coordination
customEvents
| where name in ("CancellationDetected", "ProgressUpdated", "RateLimitCheck")
| extend InstanceId = tostring(customDimensions["InstanceId"])
| summarize InstanceCount = dcount(InstanceId) by name, bin(timestamp, 5m)
| where InstanceCount > 1 // Multi-instance activity
| render timechart
```

#### **3. Rate Limiting Issues Detection**
```kusto
// Detect potential rate limit violations
exceptions
| where outerMessage contains "429" or outerMessage contains "rate limit"
| extend InstanceId = tostring(customDimensions["InstanceId"])
| summarize Count = count() by InstanceId, bin(timestamp, 1m)
| where Count > 0
```

### **Alerting Rules**
```yaml
# Azure Monitor alerts for multi-instance issues
- name: "Multiple Instances Detected"
  condition: "function_instance_count > 2"
  duration: "5 minutes"
  action: "Log info message - monitor for rate limiting issues"

- name: "Rate Limit Violations with Multiple Instances"  
  condition: "rate_limit_violations > 0 AND function_instance_count > 1"
  duration: "1 minute"
  action: "HIGH PRIORITY - Implement Redis rate limiting"

- name: "Cancellation Working Across Instances"
  condition: "cancellation_detected_instances > 1"  
  duration: "immediate"
  action: "Log success - multi-instance cancellation working correctly"
```

---

## 🎯 Decision Matrix

### **When to Implement Multi-Instance Enhancements**

| Scenario | Instance Count | Rate Limiting Issue | Action Required |
|----------|---------------|-------------------|-----------------|
| **Normal Load** | 1 instance | None | ✅ No action needed |
| **Moderate Load** | 2-3 instances | Occasional 429s | 🟡 Monitor, prepare Redis |
| **High Load** | 3+ instances consistently | Frequent 429s | 🔴 Implement Redis immediately |
| **Enterprise Load** | 5+ instances | Rate limit violations | 🔴 Redis + performance tuning |

### **Implementation Triggers**

#### **Immediate Implementation (High Priority)**
- Azure Functions consistently scaling to **3+ instances**
- Rate limit violations (HTTP 429) from BigCommerce API
- **20+ concurrent migrations** running simultaneously
- Customer reports of migration slowdowns

#### **Prepare for Implementation (Medium Priority)**  
- Occasional scaling to 2-3 instances
- Increasing migration volume trends
- Enterprise customer onboarding planned

#### **Monitor Only (Low Priority)**
- Single instance operation
- Low migration volume
- No rate limiting issues detected

---

## 📚 Reference Documents

### **Feature Documentation**
- **[Multi-Instance Cancellation](./Feature-Reference-Multi-Instance-Cancellation.md)** - How cancellation works perfectly across instances
- **[Distributed Rate Limiting Enhancement](./Feature-Enhancement-Distributed-Rate-Limiting.md)** - Complete Redis implementation plan
- **[Implementation Task Details](./Task-Distributed-Rate-Limiting-Implementation.md)** - Step-by-step implementation guide

### **Architecture Documentation**
- **[Master Task Tracking](./Master-Task-Tracking-Implementation-Roadmap.md)** - Overall project status and roadmap
- **[Architecture Documentation](./Architecture-Documentation.md)** - Core system architecture
- **[Infrastructure Deployment Guide](./Infrastructure-Deployment-Guide.md)** - Complete infrastructure setup

---

## 📝 Summary

### **Current Status: Excellent Multi-Instance Foundation**

**✅ What Works Great Already:**
- **Cancellation**: Perfect coordination via Azure Table Storage
- **Progress Tracking**: Real-time updates across all instances  
- **Entity Mapping**: Consistent data relationships
- **Error Logging**: Complete visibility via OpenSearch

**🔧 What Needs Enhancement (When Scaling):**
- **Rate Limiting**: Redis-based coordination for 3+ instances

**🎯 Key Insight:**
Your cancellation system demonstrates **the correct design pattern** for multi-instance coordination. The rate limiting enhancement simply applies this same proven pattern to solve the remaining coordination challenge.

**📊 Bottom Line:**
The system is **already 90% multi-instance ready**. The remaining 10% (rate limiting) can be implemented in 2-3 weeks when actually needed due to scaling. 