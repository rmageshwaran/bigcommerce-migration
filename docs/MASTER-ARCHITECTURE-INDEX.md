# 🏗️ **MASTER ARCHITECTURE INDEX**
## *Comprehensive Reference for All Development Work*

### 📋 **HOW TO USE THIS INDEX**
**MANDATORY**: Before ANY feature implementation, bug fix, or enhancement:
1. ✅ **Check relevant sections below** 
2. ✅ **Read referenced documents**
3. ✅ **Follow established patterns**
4. ✅ **Validate against constraints**

---

## 🎯 **CORE ARCHITECTURAL PRINCIPLES**

### **1. Design Principles**
| Principle | Document Reference | Key Constraints |
|-----------|-------------------|-----------------|
| **Azure Durable Functions Determinism** | `Azure-Durable-Functions-Deterministic-Architecture.md` | No external calls in orchestrators, use activities |
| **SignalR Centralization** | `SignalR-Centralized-Architecture.md` | Always use SignalREventFactory, never manual events |
| **Continue-on-Error** | `Consistent-Cancellation-Handling-Implementation.md` | Never stop migration for individual entity failures |
| **No Retry Logic** | `Architecture-Documentation.md` | No automatic retries to avoid API rate limit issues |
| **Test-Driven Development** | `Coding-Standards-and-Testing-Strategy.md` | All code must have 99%+ test coverage |
| **Unit Testability** | `Testing-Strategy-Guide.md` | All components must be unit testable with DI |

### **2. Performance Requirements**
| Requirement | Target | Reference Document |
|-------------|--------|--------------------|
| **Throughput** | 12,000+ req/hour | `Throughput-Optimization-Technical-Architecture.md` |
| **Error Rate** | <5% for production | `Performance-Optimization-Completion-Summary.md` |
| **Memory Usage** | Azure Functions limits | `Performance-Optimization-Detailed-Task-Breakdown.md` |
| **Rate Limiting** | Dynamic 5-50 req/sec | `Feature-Enhancement-Distributed-Rate-Limiting.md` |

---

## 🔧 **IMPLEMENTATION PATTERNS**

### **SignalR Events** ⚡
**ALWAYS USE**: `SignalREventFactory` - NEVER create events manually

```csharp
// ✅ CORRECT - Use factory
var progressEvent = _signalREventFactory.CreateMigrationProgress(migrationId, options);

// ❌ WRONG - Manual creation
var progressEvent = new MigrationProgressEvent { ... };
```

**Reference**: `SignalR-Centralization-QUICK-REFERENCE.md`

### **Error Handling** 🛡️
**ALWAYS FOLLOW**: Return proper result objects, never throw in Durable Functions

```csharp
// ✅ CORRECT - Return result
catch (OperationCanceledException)
{
    return new BatchProcessingResult 
    { 
        IsSuccess = false, 
        ErrorMessage = "Operation was cancelled" 
    };
}

// ❌ WRONG - Throwing breaks determinism
catch (OperationCanceledException)
{
    throw; // Breaks Durable Functions
}
```

**Reference**: `Consistent-Cancellation-Handling-Implementation.md`

### **API Integration** 🔌
**ALWAYS USE**: 
- Connection pooling (`SimpleOptimizedBigCommerceApiClient`)
- Batch operations where possible
- Dynamic rate limiting

**Reference**: `BigCommerce-API-Entity-Reference.md`

### **Storage Strategy** 💾
**FOLLOW PATTERN**:
- **Azure Table Storage**: Operational data (mappings, rate limits)
- **OpenSearch**: Logging and analytics
- **Blob Storage**: Large files (error payloads, reports)

**Reference**: `Storage-Strategy-and-Architecture.md`

---

## 📁 **DOCUMENT CATEGORIES**

### **🏗️ Core Architecture**
- `Architecture-Documentation.md` - Complete system overview
- `Migration-Architecture-and-Execution-Flow.md` - Migration workflows
- `Throughput-Optimization-Technical-Architecture.md` - Performance architecture

### **🔧 Implementation Guides**
- `SignalR-Centralization-QUICK-REFERENCE.md` - SignalR patterns
- `Entity-Configuration-Guide.md` - Entity processing configuration
- `Error-Logging-and-Blob-Storage-Guide.md` - Error handling patterns

### **🧪 Testing & Quality**
- `Testing-Strategy-Guide.md` - Testing approach
- `Coding-Standards-and-Testing-Strategy.md` - Code quality standards
- `Integration-and-E2E-Testing-Strategy.md` - Integration patterns

### **🚀 Deployment & Infrastructure**
- `Infrastructure-Deployment-Guide.md` - Deployment procedures
- `Docker-Local-Testing-Strategy.md` - Local development setup

### **⚡ Performance & Optimization**
- `Performance-Optimization-Completion-Summary.md` - Performance achievements
- `Throughput-Optimization-Task-Tracker.md` - Optimization tracking

---

## 🔍 **WORKFLOW CHECKLISTS**

### **Before Starting Any Feature/Fix:**
- [ ] **Read relevant architecture documents**
- [ ] **Check existing patterns in codebase**
- [ ] **Validate against performance requirements**
- [ ] **Ensure SignalR centralization compliance**
- [ ] **Plan testing strategy (TDD approach)**
- [ ] **Review error handling requirements**

### **During Implementation:**
- [ ] **Use established patterns (DI, factory pattern, etc.)**
- [ ] **Follow naming conventions**
- [ ] **Implement proper error handling**
- [ ] **Add comprehensive tests**
- [ ] **Update progress tracking if needed**

### **Before Completing:**
- [ ] **Run full test suite (562+ tests must pass)**
- [ ] **Validate performance impact**
- [ ] **Check SignalR event consistency**
- [ ] **Verify error handling coverage**
- [ ] **Update relevant documentation**

---

## 🚨 **CRITICAL CONSTRAINTS**

### **NEVER DO:**
❌ Create SignalR events manually (use factory)
❌ Throw exceptions in Durable Functions components  
❌ Add retry logic for API calls
❌ Break deterministic orchestrator patterns
❌ Skip unit tests
❌ Ignore rate limiting requirements

### **ALWAYS DO:**
✅ Use dependency injection
✅ Follow established error handling patterns
✅ Maintain 99%+ test coverage
✅ Use centralized SignalR factory
✅ Follow performance optimization patterns
✅ Validate against architectural constraints

---

## 📚 **Quick Reference Links**

| Topic | Primary Document | Quick Reference |
|-------|------------------|-----------------|
| **Overall Architecture** | `Architecture-Documentation.md` | System overview, components |
| **SignalR Patterns** | `SignalR-Centralization-QUICK-REFERENCE.md` | Factory usage, event creation |
| **Error Handling** | `Consistent-Cancellation-Handling-Implementation.md` | Exception patterns |
| **Performance** | `Throughput-Optimization-Technical-Architecture.md` | Optimization strategies |
| **Testing** | `Testing-Strategy-Guide.md` | Test structure, coverage |
| **Deployment** | `Infrastructure-Deployment-Guide.md` | Environment setup |

---

**🎯 REMEMBER**: This system has 85% completion with production-ready core components. Any changes must maintain backward compatibility and follow established patterns.

**📊 CURRENT STATUS**: 562/562 tests passing (100% success rate) - maintain this standard! 