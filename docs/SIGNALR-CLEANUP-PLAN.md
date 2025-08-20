# 🧹 SignalR Broadcasting - Detailed Cleanup Plan

**Created**: 2025-01-17  
**Phase**: 1.2 - Cleanup Strategy  
**Dependencies**: SIGNALR-ANALYSIS-REPORT.md  

---

## 🎯 Cleanup Overview

### **Cleanup Goals**
- Remove 95% of SignalR events (from ~20,000 to ~600 per migration)
- Eliminate 4 redundant broadcasting services  
- Consolidate 11 event types down to 3 essential types
- Clean up 20+ files with scattered broadcasting logic
- Simplify frontend from 3 dashboard components to 1 enhanced component

### **Cleanup Strategy**
1. **Preserve Functionality**: Keep progress tracking, only remove broadcasting
2. **Incremental Approach**: Remove broadcasting gradually while maintaining compatibility
3. **Safety First**: Extensive testing before removing each service
4. **Zero Downtime**: Implement new centralized service before removing old ones

---

## 📋 Detailed Cleanup Tasks

### **PHASE 1: Backend Broadcasting Removal** (4-5 hours)

#### **Task 1.1: ParallelProgressAggregator.cs Cleanup** (1.5h)
```csharp
File: src/BigCommerce.Migration.Infrastructure/Services/ParallelProgressAggregator.cs
Estimated Time: 1.5 hours
Risk Level: MEDIUM (heavily used service)
```

**Fields to Remove:**
```csharp
// Lines 40-42: Remove SignalR-specific fields
private int _signalRUpdateIntervalMs = 100;
private DateTime _lastSignalRUpdate = DateTime.MinValue;
private readonly object _signalRRateLimitLock = new object();
private readonly IProgressEventPublisher? _progressEventPublisher;
```

**Methods to Remove:**
```csharp
// Lines 870-884: Remove rate-limited SignalR updates
private async Task SendRateLimitedSignalRUpdateAsync(CancellationToken cancellationToken)

// Lines 890-955: Remove main SignalR broadcasting method
private async Task SendSignalRUpdateAsync(bool force, CancellationToken cancellationToken)

// Lines 688-692: Remove SignalR interval configuration
public void ConfigureSignalRUpdateInterval(int minimumIntervalMs)
```

**Constructor Changes:**
```csharp
// Remove IProgressEventPublisher dependency
public ParallelProgressAggregator(
    string migrationId,
    string entityType,
    IDateTimeProvider dateTimeProvider,
    ILogger<ParallelProgressAggregator> logger,
    // IProgressEventPublisher? progressEventPublisher = null, // REMOVE THIS LINE
    // ISignalREventFactory? signalREventFactory = null // REMOVE THIS LINE
)
```

**Method Call Removals:**
```csharp
// Lines 381-382: Remove from ReportSubBatchProgress
// await _progressEventPublisher?.PublishMigrationProgressAsync(progressEvent, cancellationToken);

// Lines 938-939: Remove from SendSignalRUpdateAsync calls
// await SendSignalRUpdateAsync(force: true, cancellationToken);
```

**Interface Updates:**
```csharp
// File: src/BigCommerce.Migration.Core/Interfaces/IParallelProgressAggregator.cs
// Remove method:
void ConfigureSignalRUpdateInterval(int minimumIntervalMs);
```

#### **Task 1.2: PipelineProgressAggregator.cs Cleanup** (1h)
```csharp
File: src/BigCommerce.Migration.Infrastructure/Services/PipelineProgressAggregator.cs
Estimated Time: 1 hour
Risk Level: LOW (less critical service)
```

**Fields to Remove:**
```csharp
// Remove broadcasting-related fields
private readonly IProgressEventPublisher _progressEventPublisher;
private readonly ISignalREventFactory _signalREventFactory;
private CancellationTokenSource? _broadcastCancellationTokenSource;
private Task? _broadcastTask;
```

**Methods to Remove:**
```csharp
// Lines 155-174: Remove real-time broadcasting
public async Task StartRealtimeBroadcastingAsync(CancellationToken cancellationToken = default)

// Lines 179-202: Remove broadcasting stop method
public async Task StopRealtimeBroadcastingAsync()

// Lines 275-294: Remove continuous broadcasting loop
private async Task BroadcastProgressContinuouslyAsync(CancellationToken cancellationToken)

// Lines 296-324: Remove comprehensive progress broadcasting
private async Task BroadcastComprehensiveProgressAsync(CancellationToken cancellationToken)
```

**Constructor Changes:**
```csharp
// Remove SignalR dependencies
public PipelineProgressAggregator(
    string migrationId,
    string parentEntityType,
    ILogger<PipelineProgressAggregator> logger,
    // IProgressEventPublisher progressEventPublisher, // REMOVE
    // ISignalREventFactory signalREventFactory // REMOVE
)
```

**Interface Updates:**
```csharp
// File: src/BigCommerce.Migration.Core/Interfaces/IPipelineProgressAggregator.cs
// Remove methods:
Task StartRealtimeBroadcastingAsync(CancellationToken cancellationToken = default);
Task StopRealtimeBroadcastingAsync();
```

#### **Task 1.3: UniversalMigrationProgressAggregator.cs Cleanup** (1h)
```csharp
File: src/BigCommerce.Migration.Infrastructure/Services/UniversalMigrationProgressAggregator.cs
Estimated Time: 1 hour
Risk Level: LOW (aggregation service)
```

**Fields to Remove:**
```csharp
// Remove broadcasting infrastructure
private readonly IProgressEventPublisher _progressEventPublisher;
private readonly ISignalREventFactory _signalREventFactory;
private readonly ConcurrentDictionary<string, CancellationTokenSource> _broadcastCancellationTokens;
private readonly ConcurrentDictionary<string, Task> _broadcastTasks;
```

**Methods to Remove:**
```csharp
// Lines 254-283: Remove progress broadcasting start
public async Task StartProgressBroadcastingAsync(string migrationId, CancellationToken cancellationToken = default)

// Lines 288-310: Remove broadcasting stop
public async Task StopProgressBroadcastingAsync(string migrationId)

// Lines 315-342: Remove broadcasting loop
private async Task BroadcastProgressContinuouslyAsync(string migrationId, CancellationToken cancellationToken)

// Lines 344-367: Remove universal progress broadcasting
private async Task BroadcastUniversalProgressAsync(string migrationId, CancellationToken cancellationToken)
```

**Interface Updates:**
```csharp
// File: src/BigCommerce.Migration.Core/Interfaces/IUniversalMigrationProgressAggregator.cs
// Remove methods:
Task StartProgressBroadcastingAsync(string migrationId, CancellationToken cancellationToken = default);
Task StopProgressBroadcastingAsync(string migrationId);
```

#### **Task 1.4: ProgressTracker.cs Cleanup** (1h)
```csharp
File: src/BigCommerce.Migration.Activities/Services/ProgressTracker.cs
Estimated Time: 1 hour
Risk Level: MEDIUM (core progress tracking)
```

**Methods to Remove:**
```csharp
// Lines 601-635: Remove migration progress event publishing
private async Task PublishMigrationProgressEventAsync(string migrationId, MigrationProgress progress, CancellationToken cancellationToken)

// Lines 656-684: Remove entity progress event publishing calls
// Keep entity progress tracking, remove SignalR publishing
```

**Field Updates:**
```csharp
// Keep IProgressEventPublisher for error events only
// Remove usage for migration and entity progress events
```

**Method Call Removals:**
```csharp
// Remove calls to PublishMigrationProgressEventAsync
// Keep error event publishing for debugging
```

### **PHASE 2: Event Type Cleanup** (1-2 hours)

#### **Task 2.1: SignalREventFactory.cs Cleanup** (1h)
```csharp
File: src/BigCommerce.Migration.Core/Services/SignalREventFactory.cs
Estimated Time: 1 hour
Risk Level: LOW (factory service)
```

**Interface Methods to Remove:**
```csharp
// Remove unused event creation methods
BatchProgressEvent CreateBatchProgress(string migrationId, BatchProgressOptions options);
SubBatchStartedEvent CreateSubBatchStarted(string migrationId, SubBatchStartedOptions options);
SubBatchCompletedEvent CreateSubBatchCompleted(string migrationId, SubBatchCompletedOptions options);
SubBatchMigrationProgressEvent CreateSubBatchProgress(string migrationId, SubBatchProgressOptions options);
```

**Implementation Methods to Remove:**
```csharp
// Lines 142-176: Remove CreateBatchProgress implementation
public BatchProgressEvent CreateBatchProgress(string migrationId, BatchProgressOptions options)

// Remove CreateSubBatch* implementations (if they exist)
```

**Keep Essential Methods:**
```csharp
// KEEP: Core event types
CreateMigrationProgress() - For centralized migration events
CreateEntityProgress() - For chunk-level entity events  
CreateErrorProgress() - For error events
CreateStatusProgress() - For lifecycle events (start/complete/cancel)

// KEEP: System events (separate concerns)
CreateQuotaUpdate() - For quota monitoring
CreatePredictiveRateLimit() - For rate limiting
CreateSystemHealth() - For health monitoring
```

#### **Task 2.2: Event Model Cleanup** (30min)
```csharp
Files to Remove:
- src/BigCommerce.Migration.Core/Models/BatchProgressEvent.cs
- src/BigCommerce.Migration.Core/Models/SubBatchStartedEvent.cs  
- src/BigCommerce.Migration.Core/Models/SubBatchCompletedEvent.cs
- src/BigCommerce.Migration.Core/Models/SubBatchMigrationProgressEvent.cs
```

**ProgressEvent.cs Updates:**
```csharp
// File: src/BigCommerce.Migration.Core/Models/ProgressEvent.cs
// Remove polymorphic type declarations:
[JsonDerivedType(typeof(BatchProgressEvent), "batch")] // REMOVE
[JsonDerivedType(typeof(SubBatchStartedEvent), "subbatch-started")] // REMOVE
[JsonDerivedType(typeof(SubBatchCompletedEvent), "subbatch-completed")] // REMOVE
[JsonDerivedType(typeof(SubBatchMigrationProgressEvent), "subbatch-progress")] // REMOVE
```

### **PHASE 3: Frontend Cleanup** (2-3 hours)

#### **Task 3.1: SignalR Service Cleanup** (1h)
```typescript
File: src/BigCommerce.Migration.Dashboard/src/services/signalRService.ts
Estimated Time: 1 hour
Risk Level: MEDIUM (core frontend service)
```

**Event Handlers to Remove:**
```typescript
// Lines 299-305: Remove batch progress handler
this.connection.on('BatchProgressUpdated', (eventData: any) => { ... });

// Remove sub-batch handlers (if any)
this.connection.on('SubBatchStarted', ...);
this.connection.on('SubBatchCompleted', ...);
this.connection.on('SubBatchProgress', ...);

// Simplify status handling - keep only for lifecycle events
// Remove redundant status progress events
```

**Event Listeners to Simplify:**
```typescript
// Keep essential handlers:
✅ 'MigrationProgressUpdated' - Core progress events
✅ 'EntityProgressUpdated' - Chunk-level entity events  
✅ 'MigrationStatusChanged' - Lifecycle events (start/complete/cancel)
✅ 'ErrorOccurred' - Error events
✅ System health events - Independent monitoring

// Remove/simplify:
❌ Complex event enrichment logic for removed events
❌ Legacy event compatibility handlers
❌ Duplicate event forwarding
```

#### **Task 3.2: Dashboard Component Consolidation** (1.5h)
```typescript
Target: Enhance MigrationOverview.tsx as primary component
Estimated Time: 1.5 hours  
Risk Level: LOW (UI enhancement)
```

**Component Analysis:**
```typescript
// MigrationOverview.tsx (592 lines) - KEEP as primary
Status: Enhance with real-time entity progress
Action: Add clean layout matching target screenshot
Priority: P0 (main dashboard component)

// EnhancedMigrationDashboard.tsx (814 lines) - EVALUATE
Status: Extract reusable components
Action: Merge best features into MigrationOverview
Priority: P1 (contains useful enhancements)

// RealTimeMigrationDashboard.tsx (586 lines) - CONSIDER REMOVAL
Status: Overlaps with enhanced dashboard
Action: Extract real-time hooks, remove component
Priority: P2 (functionality can be merged)
```

**Specific Cleanup Actions:**
```typescript
// Extract reusable components from EnhancedMigrationDashboard:
- Entity progress row component
- Progress bar with status indicators  
- Real-time connection status
- Migration header with store information

// Remove redundant functionality:
- Duplicate progress calculations
- Multiple SignalR connection handlers
- Overlapping state management
```

#### **Task 3.3: Hook and Type Cleanup** (30min)
```typescript
Files to Update:
- src/BigCommerce.Migration.Dashboard/src/hooks/useRealTimeMigrationProgress.ts
- src/BigCommerce.Migration.Dashboard/src/hooks/useDetailedMigrationProgress.ts
- src/BigCommerce.Migration.Dashboard/src/types/index.ts
```

**Type Definitions to Remove:**
```typescript
// Remove interfaces for deleted event types:
interface BatchProgressEvent { ... } // REMOVE
interface SubBatchStartedEvent { ... } // REMOVE
interface SubBatchCompletedEvent { ... } // REMOVE
interface SubBatchProgressEvent { ... } // REMOVE
```

**Hook Simplification:**
```typescript
// Remove handling for deprecated events in hooks
// Simplify event processing logic
// Update TypeScript types for new event structure
```

### **PHASE 4: Configuration Cleanup** (30min)

#### **Task 4.1: Service Registration Cleanup**
```csharp
Files to Update:
- src/BigCommerce.Migration.Functions/Extensions/ServiceCollectionExtensions.cs
- src/BigCommerce.Migration.Infrastructure/Extensions/ServiceCollectionExtensions.cs
```

**Service Registration Updates:**
```csharp
// Remove or update service registrations for cleaned services
// Update dependency injection for services that lost SignalR dependencies
// Ensure centralized broadcasting service is properly registered
```

#### **Task 4.2: Configuration Model Cleanup**
```csharp
File: src/BigCommerce.Migration.Core/Models/ParallelProcessingModels.cs
```

**Properties to Remove:**
```csharp
// Remove SignalR configuration properties
[JsonPropertyName("signalRUpdateIntervalMs")]
public int SignalRUpdateIntervalMs { get; set; } = 250; // REMOVE THIS
```

---

## 🧪 Testing Strategy

### **Testing Phases**

#### **Phase 1: Unit Testing**
- [ ] Test each cleaned service independently
- [ ] Verify progress tracking still works without broadcasting
- [ ] Test that essential functionality is preserved
- [ ] Verify error handling still works

#### **Phase 2: Integration Testing**  
- [ ] Test migration workflow end-to-end
- [ ] Verify no SignalR events from removed sources
- [ ] Test with centralized broadcasting service (when implemented)
- [ ] Verify UI still receives necessary progress updates

#### **Phase 3: Performance Testing**
- [ ] Measure SignalR event reduction (target: 95% fewer events)
- [ ] Verify no performance degradation in core functionality
- [ ] Test with multiple concurrent migrations
- [ ] Monitor memory usage and CPU impact

### **Testing Checklist**

#### **Backend Testing:**
- [ ] ParallelProgressAggregator still calculates progress correctly
- [ ] PipelineProgressAggregator still tracks sub-entity progress
- [ ] UniversalMigrationProgressAggregator still aggregates data
- [ ] ProgressTracker still persists progress to storage
- [ ] All services start and dispose cleanly without SignalR code

#### **Frontend Testing:**
- [ ] Dashboard still loads and displays migrations
- [ ] Real-time updates work with remaining event types
- [ ] Progress bars still update correctly
- [ ] Error handling still works
- [ ] No JavaScript errors from removed event handlers

#### **End-to-End Testing:**
- [ ] Start migration and verify status updates
- [ ] Cancel migration and verify cancellation works
- [ ] Complete migration and verify completion status
- [ ] Multiple concurrent migrations work correctly
- [ ] Network disconnection/reconnection scenarios

---

## ⚠️ Risk Mitigation

### **Identified Risks**

#### **Risk 1: Breaking Existing Functionality**
**Probability**: MEDIUM  
**Impact**: HIGH  
**Mitigation**: 
- Preserve all non-SignalR functionality
- Extensive unit testing before each cleanup
- Feature flag to revert changes if needed

#### **Risk 2: UI Components Breaking**
**Probability**: LOW  
**Impact**: MEDIUM  
**Mitigation**:
- Keep essential event types during cleanup
- Test frontend after each backend change
- Gradual component consolidation

#### **Risk 3: Missing Progress Updates**
**Probability**: LOW  
**Impact**: HIGH  
**Mitigation**:
- Implement centralized broadcasting before removing old system
- Monitor event frequency during cleanup
- Keep error event broadcasting for debugging

### **Rollback Strategy**

#### **Emergency Rollback Plan:**
1. **Git Tags**: Tag before each major cleanup phase
2. **Feature Flags**: Ability to re-enable old broadcasting
3. **Progressive Cleanup**: Clean one service at a time
4. **Monitoring**: Track event counts and error rates

#### **Rollback Triggers:**
- SignalR event count doesn't decrease as expected
- UI stops updating progress
- Migration functionality breaks
- Performance degrades significantly

---

## 📊 Success Metrics

### **Quantitative Metrics**
- **Event Reduction**: Target 95% fewer SignalR events per migration
- **Code Reduction**: Remove 500+ lines of broadcasting code
- **File Count**: Remove 4-6 event model files
- **Service Simplification**: Remove broadcasting from 4 services

### **Qualitative Metrics**
- **Code Maintainability**: Single place to debug SignalR issues
- **Performance**: Smoother UI updates with consistent intervals
- **Reliability**: No more duplicate/conflicting events
- **Developer Experience**: Cleaner service interfaces

### **Validation Checklist**
- [ ] SignalR event frequency reduced by 90%+
- [ ] All core migration functionality preserved
- [ ] UI updates still work correctly
- [ ] No regression in error handling
- [ ] Performance improved or maintained
- [ ] Code complexity significantly reduced

---

## 📅 Cleanup Timeline

### **Day 1: Backend Service Cleanup (4-5h)**
- **Morning (2h)**: ParallelProgressAggregator and PipelineProgressAggregator
- **Afternoon (2-3h)**: UniversalMigrationProgressAggregator and ProgressTracker

### **Day 2: Event Type and Frontend Cleanup (3-4h)**
- **Morning (1-2h)**: SignalREventFactory and event model cleanup
- **Afternoon (2h)**: Frontend SignalR service and component cleanup

### **Day 3: Testing and Validation (2-3h)**
- **Morning (1h)**: Unit and integration testing
- **Afternoon (1-2h)**: End-to-end testing and performance validation

**Total Estimated Time**: 9-12 hours over 3 days

---

## 🎯 Next Steps

### **Immediate Actions**
1. **Review and Approve Plan**: Get team approval for cleanup approach
2. **Create Feature Branch**: Set up dedicated branch for cleanup work
3. **Begin Phase 1**: Start with ParallelProgressAggregator cleanup
4. **Test Each Step**: Verify functionality after each service cleanup

### **Dependencies**
- **Centralized Service**: Implement before removing old broadcasting
- **Team Coordination**: Ensure no concurrent changes to affected services
- **Testing Environment**: Ensure test environment available for validation

### **Success Criteria**
- All cleanup tasks completed without breaking functionality
- 95% reduction in SignalR events achieved
- Clean, maintainable codebase ready for centralized broadcasting
- Team confident in new architecture

---

*Cleanup Plan Created: 2025-01-17*  
*Ready for Implementation: Phase 1 Backend Cleanup*  
*Next Document: Implementation Progress Tracker*