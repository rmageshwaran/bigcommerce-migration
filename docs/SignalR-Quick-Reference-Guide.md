# SignalR Queue-Based Implementation - Quick Reference Guide

## 🚀 **Quick Start Resume Guide**

**Last Updated:** January 2025  
**Purpose:** Jump back into implementation from any point  
**Documents:** Use with [Detailed Tasks](./SignalR-Implementation-Detailed-Tasks.md) & [Status Tracker](./SignalR-Implementation-Status-Tracker.md)

---

## 📊 **Current Status Quick Check**

### **Check Your Progress:**
1. **Open:** `docs/SignalR-Implementation-Status-Tracker.md`
2. **Find:** Last completed task
3. **Next:** Find next "⏸️ Pending" task
4. **Start:** Mark as "🔄 In Progress"

### **Current Phase Status:**
- **Phase 1: Backend** - 0% Complete (18 tasks)
- **Phase 2: Frontend** - 17% Complete (10 pending tasks)  
- **Phase 3: Testing** - 0% Complete (10 tasks)
- **Phase 4: Documentation** - 0% Complete (5 tasks)

---

## 🎯 **Implementation Phases Overview**

### **Phase 1: Backend Implementation (Days 1-3)**
```
Stage 1.1: Models & Interfaces (4 tasks) → Foundation
Stage 1.2: Activity Functions (5 tasks) → Queue Operations
Stage 1.3: Queue Processors (4 tasks) → SignalR Broadcasting
Stage 1.4: Orchestrator Updates (5 tasks) → Remove SignalR Calls
```

### **Phase 2: Frontend Enhancement (Day 3-4)**
```
Stage 2.1: Connection Management (4 tasks) → Queue-Aware State
Stage 2.2: Enhanced Fallback (4 tasks) → Adaptive Polling
Stage 2.3: UI Enhancements (4 tasks) → Visual Indicators
```

### **Phase 3: Testing & Validation (Day 4-5)**
```
Stage 3.1: Unit Tests (4 tasks) → Component Testing
Stage 3.2: Integration Tests (3 tasks) → End-to-End Flow
Stage 3.3: System Tests (3 tasks) → Full Validation
```

### **Phase 4: Documentation (Day 5)**
```
Final documentation and deployment guides (5 tasks)
```

---

## 🔧 **Ready-to-Start Tasks**

### **✅ Can Start Immediately (No Dependencies):**

#### **BACK-001-01: Create ProgressEvent Model** ⏸️
- **File:** `src/BigCommerce.Migration.Core/Models/ProgressEvent.cs`
- **Time:** 30 minutes
- **What to do:** Create core progress event class with all properties

#### **BACK-001-04: Add Queue Configuration Model** ⏸️  
- **File:** `src/BigCommerce.Migration.Core/Models/QueueConfiguration.cs`
- **Time:** 15 minutes
- **What to do:** Configuration class for queue settings

#### **BACK-004-01: Audit Current SignalR Calls** ⏸️
- **File:** Multiple orchestrator files
- **Time:** 30 minutes  
- **What to do:** Find and document all SignalR broadcast calls

#### **FRONT-002-01: Enhance Connection State** ⏸️
- **File:** `src/BigCommerce.Migration.Dashboard/src/types/index.ts`
- **Time:** 20 minutes
- **What to do:** Add queue-aware connection state interface

#### **FRONT-003-01: Create Adaptive Polling Config** ⏸️
- **File:** `src/BigCommerce.Migration.Dashboard/src/config/pollingConfig.ts`
- **Time:** 15 minutes
- **What to do:** Configuration for adaptive polling intervals

---

## 📁 **Key Files & Locations**

### **Backend Core Files:**
```
📂 src/BigCommerce.Migration.Core/
├── Models/
│   ├── ProgressEvent.cs                    ← NEW: BACK-001-01
│   └── QueueConfiguration.cs               ← NEW: BACK-001-04
├── Interfaces/
│   └── IQueueProgressService.cs            ← NEW: BACK-001-03
└── Extensions/
    └── ProgressEventExtensions.cs          ← NEW: BACK-001-02
```

### **Backend Activity Functions:**
```
📂 src/BigCommerce.Migration.Orchestration/Activities/
├── QueueProgressEventActivity.cs           ← NEW: BACK-002-01
├── QueueBatchEventActivity.cs              ← NEW: BACK-002-02
├── QueueStatusEventActivity.cs             ← NEW: BACK-002-03
└── QueueActivityErrorHandler.cs            ← NEW: BACK-002-04
```

### **Backend Queue Processors:**
```
📂 src/BigCommerce.Migration.Functions/Functions/
├── ProgressQueueProcessor.cs               ← NEW: BACK-003-01
├── BatchQueueProcessor.cs                  ← NEW: BACK-003-02
└── StatusQueueProcessor.cs                 ← NEW: BACK-003-03
```

### **Frontend Files:**
```
📂 src/BigCommerce.Migration.Dashboard/src/
├── types/index.ts                          ← UPDATE: FRONT-002-01
├── config/pollingConfig.ts                 ← NEW: FRONT-003-01
├── hooks/
│   └── useQueueActivityDetection.ts        ← NEW: FRONT-003-02
└── services/signalRService.ts              ← UPDATE: FRONT-002-02
```

### **Orchestrator Files to Update:**
```
📂 src/BigCommerce.Migration.Orchestration/Orchestrators/
├── MigrationOrchestrator.cs                ← UPDATE: BACK-004-02
└── EntityMigrationOrchestrator.cs          ← UPDATE: BACK-004-03
```

---

## 💡 **Implementation Patterns**

### **Queue Event Pattern:**
```csharp
// BEFORE (in orchestrators - deterministic violation):
await _signalRService.BroadcastProgressUpdateAsync(migrationId, progress);

// AFTER (queue-based - deterministic):
await context.CallActivityAsync("QueueProgressEvent", new ProgressEvent 
{
    MigrationId = migrationId,
    EventType = "MigrationProgress",
    EntityType = entityType,
    EventData = progress,
    Timestamp = DateTime.UtcNow
});
```

### **Queue Processor Pattern:**
```csharp
[FunctionName("ProcessProgressUpdate")]
public async Task ProcessProgressUpdate(
    [QueueTrigger("progress-updates")] ProgressEvent evt)
{
    try
    {
        // Target specific migration group
        var groupName = $"migration-{evt.MigrationId}";
        await _signalRService.BroadcastToGroup(groupName, evt.EventType, evt.EventData);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to broadcast {EventType} for {MigrationId}", 
            evt.EventType, evt.MigrationId);
        // Dead letter queue handles retries
    }
}
```

### **Frontend Pattern:**
```typescript
// Enhanced connection with auto-rejoin
useEffect(() => {
  if (connectionState === 'Connected') {
    // Auto-rejoin active migration groups
    activeMigrations.forEach(migrationId => {
      signalRService.joinMigrationGroup(migrationId);
    });
  }
}, [connectionState, activeMigrations]);
```

---

## 🔍 **Troubleshooting Quick Fixes**

### **Build Errors:**
```bash
# Missing references
dotnet add package Microsoft.Azure.Functions.Worker.Extensions.Storage

# Clean build
dotnet clean && dotnet build
```

### **Queue Connection Issues:**
```json
// Check appsettings.json
{
  "AzureWebJobsStorage": "DefaultEndpointsProtocol=https;AccountName=...",
  "QueueNames": {
    "ProgressUpdateQueueName": "progress-updates"
  }
}
```

### **SignalR Issues:**
```typescript
// Check connection state
console.log('SignalR State:', signalRService.getConnectionState());

// Test group joining
await signalRService.joinMigrationGroup(migrationId);
```

---

## ⚡ **Daily Workflow**

### **Starting Work:**
1. **Check Status:** Open `SignalR-Implementation-Status-Tracker.md`
2. **Find Next Task:** Look for next "⏸️ Pending" task
3. **Update Status:** Change to "🔄 In Progress"  
4. **Start Timer:** Use estimated time for focus
5. **Update TODOs:** Mark current todo as "in_progress"

### **Completing Work:**
1. **Mark Complete:** Change task to "✅ Complete"
2. **Update Progress:** Update percentage in overview table
3. **Add Notes:** Document any issues or discoveries
4. **Update TODOs:** Mark todo as "completed"
5. **Commit Code:** Git commit with task ID reference

### **Blocked/Issues:**
1. **Mark Blocked:** Change to "❌ Blocked"
2. **Document Issue:** Add detailed notes about problem
3. **Update TODOs:** Mark todo as "pending" with blocker info
4. **Seek Help:** Reference this guide or ask for assistance

---

## 📋 **Testing Quick Commands**

### **Backend Tests:**
```bash
# Run queue activity tests
dotnet test tests/BigCommerce.Migration.UnitTests/Orchestration/Activities/

# Run function tests  
dotnet test tests/BigCommerce.Migration.UnitTests/Functions/

# Run integration tests
dotnet test tests/BigCommerce.Migration.IntegrationTests/
```

### **Frontend Tests:**
```bash
# Run hook tests
npm test -- hooks/__tests__/

# Run service tests
npm test -- services/__tests__/

# Run component tests
npm test -- components/Tests/
```

### **Manual Testing:**
```bash
# Start backend
cd src/BigCommerce.Migration.Functions
func start

# Start frontend
cd src/BigCommerce.Migration.Dashboard  
npm run dev

# Test SignalR
Open http://localhost:3000/test/signalr
```

---

## 🎯 **Critical Success Checkpoints**

### **Phase 1 Complete When:**
- [ ] All orchestrators have NO SignalR calls
- [ ] Queue processors handle all event types
- [ ] Activity functions queue events successfully
- [ ] Deterministic validation passes

### **Phase 2 Complete When:**
- [ ] Frontend handles queue-based events
- [ ] Adaptive polling works correctly
- [ ] Auto-reconnect functionality works
- [ ] All UI components display properly

### **Phase 3 Complete When:**
- [ ] All unit tests pass
- [ ] Integration tests validate end-to-end flow
- [ ] Scaling tests show no duplicates
- [ ] Resilience tests pass

### **Phase 4 Complete When:**
- [ ] Architecture documentation updated
- [ ] Configuration guides complete
- [ ] Deployment checklist validated
- [ ] Status tracker finalized

---

## 🚨 **Emergency Recovery**

### **If Implementation Breaks:**
1. **Revert Last Changes:** `git revert HEAD`
2. **Check Status Tracker:** Find last working state
3. **Restart from Known Good:** Mark previous task as "⏸️ Pending"
4. **Re-run Tests:** Validate current state
5. **Continue Carefully:** Smaller incremental changes

### **If Confused About Next Steps:**
1. **Check Dependencies:** Ensure prerequisite tasks are complete
2. **Review Acceptance Criteria:** Focus on specific requirements
3. **Look at Examples:** Reference implementation patterns above
4. **Start Simple:** Create minimal version first, then enhance

### **If Tests Fail:**
1. **Check Connections:** Verify queue and SignalR connections
2. **Review Logs:** Look for error messages and stack traces
3. **Validate Configuration:** Ensure appsettings.json is correct
4. **Test Incrementally:** Isolate the failing component

---

## 📞 **Quick Reference Contacts**

### **Documentation:**
- **Main Plan:** `docs/SignalR-Queue-Based-Implementation-Plan.md`
- **Detailed Tasks:** `docs/SignalR-Implementation-Detailed-Tasks.md` 
- **Status Tracker:** `docs/SignalR-Implementation-Status-Tracker.md`
- **This Guide:** `docs/SignalR-Quick-Reference-Guide.md`

### **Key Configuration Files:**
- **Backend:** `src/BigCommerce.Migration.Functions/appsettings.json`
- **Frontend:** `src/BigCommerce.Migration.Dashboard/src/config/environment.ts`
- **Tests:** `tests/*/appsettings.json`

---

**🎯 Ready to Resume? Start with the next "⏸️ Pending" task in your status tracker!** 