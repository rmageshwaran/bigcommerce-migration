# Durable Functions Cancellation Fix - Quick Reference Guide

## 🎯 **Phase Overview**

| Phase | Priority | Duration | Tasks | Key Goal |
|-------|----------|----------|-------|----------|
| **Phase 1** | 🔴 **CRITICAL** | 3-4 days | 4 tasks | Fix orchestrator replay determinism |
| **Phase 2** | 🟠 **HIGH** | 4-5 days | 4 tasks | Stop long-running activities |
| **Phase 3** | 🟠 **HIGH** | 3-4 days | 3 tasks | Prevent multiple instances |
| **Phase 4** | 🟡 **MEDIUM** | 2-3 days | 3 tasks | Fix progress update consistency |
| **Phase 5** | 🟡 **MEDIUM** | 2-3 days | 3 tasks | Standardize queue message handling |
| **Phase 6** | 🟢 **ENHANCEMENT** | 4-5 days | 3 tasks | Native .NET cancellation integration |

---

## 📋 **Task Breakdown by Phase**

### **Phase 1: Orchestrator Replay Determinism** ⚡ 
> **CRITICAL**: Must fix first - violates Durable Functions core requirements

| Task | Estimate | Description | Dependencies |
|------|----------|-------------|--------------|
| **1.1** | 4-6 hours | Create `DeterministicCancellationState` class and models | None |
| **1.2** | 6-8 hours | Implement replay-safe cancellation pattern | Task 1.1 |
| **1.3** | 8-10 hours | Update all orchestrators to use new pattern | Task 1.2 |
| **1.4** | 4-6 hours | Add comprehensive unit tests for determinism | Task 1.3 |

### **Phase 2: Activity-Level Cancellation** 🎯
> **HIGH**: Stop activities from continuing after cancellation

| Task | Estimate | Description | Dependencies |
|------|----------|-------------|--------------|
| **2.1** | 6-8 hours | Add periodic cancellation checks to `ProcessEntityBatchActivity` | Phase 1 |
| **2.2** | 8-10 hours | Make API requests cancellation-aware | Task 2.1 |
| **2.3** | 6-8 hours | Add cancellation to entity fetch/transform/create strategies | Task 2.2 |
| **2.4** | 4-6 hours | Create reusable cancellation middleware | Task 2.3 |

### **Phase 3: Instance Protection** 🛡️
> **HIGH**: Prevent multiple orchestrator instances running simultaneously

| Task | Estimate | Description | Dependencies |
|------|----------|-------------|--------------|
| **3.1** | 8-10 hours | Implement Azure Storage distributed locks | Phase 1 |
| **3.2** | 6-8 hours | Add instance heartbeat mechanism | Task 3.1 |
| **3.3** | 4-6 hours | Create orphaned instance cleanup service | Task 3.2 |

### **Phase 4: Progress Update Consistency** 📊
> **MEDIUM**: Prevent progress updates bypassing cancellation

| Task | Estimate | Description | Dependencies |
|------|----------|-------------|--------------|
| **4.1** | 4-6 hours | Add cancellation checks to progress activities | Phase 2 |
| **4.2** | 6-8 hours | Make SignalR broadcasts cancellation-aware | Task 4.1 |
| **4.3** | 4-6 hours | Create progress state validation | Task 4.2 |

### **Phase 5: Queue Message Consistency** 📨
> **MEDIUM**: Ensure all queue processors respect cancellation

| Task | Estimate | Description | Dependencies |
|------|----------|-------------|--------------|
| **5.1** | 4-6 hours | Audit all queue processors for cancellation gaps | Phase 2 |
| **5.2** | 6-8 hours | Create standard cancellation middleware for queues | Task 5.1 |
| **5.3** | 4-6 hours | Add cancellation headers to queue messages | Task 5.2 |

### **Phase 6: Native CancellationToken Integration** 🔧
> **ENHANCEMENT**: Full .NET cancellation support

| Task | Estimate | Description | Dependencies |
|------|----------|-------------|--------------|
| **6.1** | 8-10 hours | Add `CancellationToken` to API client methods | All phases |
| **6.2** | 6-8 hours | Implement token propagation in services | Task 6.1 |
| **6.3** | 4-6 hours | Add cancellation to database operations | Task 6.2 |

---

## 🚨 **Critical Path Analysis**

### **Must Do First (Days 1-4)**:
- ✅ **Phase 1** - Fixes fundamental determinism violation
- Without this: Unpredictable cancellation behavior continues

### **High Impact (Days 5-13)**:
- 🎯 **Phase 2** - Stops long-running operations 
- 🛡️ **Phase 3** - Prevents instance conflicts
- These solve 80% of user-visible issues

### **Polish & Enhancement (Days 14-24)**:
- 📊 **Phase 4** - UI consistency improvements
- 📨 **Phase 5** - Background processing refinements  
- 🔧 **Phase 6** - Architecture modernization

---

## 🎯 **Success Checkpoints**

### **After Phase 1**:
- [ ] No `NonDeterministicOrchestrationException` errors
- [ ] Orchestrator replay tests pass consistently
- [ ] Cancellation state preserved across restarts

### **After Phase 2**:
- [ ] Activities stop within 30 seconds of cancellation
- [ ] No BigCommerce API calls after cancellation
- [ ] Resource cleanup completes successfully

### **After Phase 3**:
- [ ] Zero duplicate orchestrator instances
- [ ] Instance collision detection works 100%
- [ ] Orphaned instances cleaned up automatically

### **Final System Validation**:
- [ ] **Zero cancelled migrations continuing to completion**
- [ ] UI matches backend state within 10 seconds
- [ ] Cancellation effective within 30 seconds maximum
- [ ] No resource leaks or zombie processes

---

## 🛠️ **Implementation Commands**

### **Start Phase 1**:
```bash
# Mark first task as in progress
# Begin with deterministic state management
git checkout -b feature/phase1-deterministic-cancellation
```

### **Testing Each Phase**:
```bash
# Run specific test suites
dotnet test --filter "Category=DeterministicCancellation"
dotnet test --filter "Category=ActivityCancellation"
dotnet test --filter "Category=InstanceProtection"
```

### **Deployment Strategy**:
```bash
# Deploy with feature flags for gradual rollout
# Test in dev → staging → production
# Monitor cancellation effectiveness metrics
```

---

## 📞 **Emergency Rollback Plan**

If any phase causes issues:

1. **Immediate**: Disable feature flags
2. **Short-term**: Revert to previous deployment
3. **Long-term**: Fix issues and redeploy

**Rollback Commands**:
```bash
# Revert feature flags
az appconfig kv set --name "cancellation-fix-enabled" --value "false"

# Emergency deployment rollback
az functionapp deployment slot swap --name bigcommerce-functions --slot staging
```

---

**Ready to start Phase 1? Let's fix this systematically! 🚀** 