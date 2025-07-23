# SignalR Queue-Based Implementation - Detailed Task Breakdown

## 📋 Document Overview

**Version:** 1.1  
**Created:** January 2025  
**Updated:** January 2025 - Added Phase 0 Cleanup  
**Purpose:** Granular task breakdown for queue-based SignalR implementation  
**Update Frequency:** After each task completion  
**Total Tasks:** 53 sub-tasks across 5 phases  

---

## 📊 **Quick Progress Overview**

| **Phase** | **Sub-Tasks** | **Completed** | **In Progress** | **Pending** | **Progress** |
|-----------|---------------|---------------|-----------------|-------------|--------------|
| **Phase 0: Cleanup** | 8 tasks | 0 | 0 | 8 | 0% |
| **Phase 1: Backend** | 18 tasks | 0 | 0 | 18 | 0% |
| **Phase 2: Frontend** | 12 tasks | 2 | 0 | 10 | 17% |
| **Phase 3: Testing** | 10 tasks | 0 | 0 | 10 | 0% |
| **Phase 4: Documentation** | 5 tasks | 0 | 0 | 5 | 0% |
| **🎯 OVERALL** | **53 tasks** | **2** | **0** | **51** | **4%** |

---

## 🧹 **Phase 0: Cleanup Existing SignalR Implementation (8 Tasks)**

### **Stage 0.1: Remove Broken SignalR Services (4 tasks)**

#### **CLEAN-001: Remove EnhancedMigrationSignalRService** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Functions/Services/EnhancedMigrationSignalRService.cs`
- **Time:** 15 minutes
- **Description:** Delete the over-engineered enhanced SignalR service
- **Acceptance Criteria:**
  - [ ] Delete EnhancedMigrationSignalRService.cs file completely
  - [ ] Remove IEnhancedMigrationSignalRService interface
  - [ ] Check for any remaining references and remove them
  - [ ] Verify no compilation errors after removal
- **Dependencies:** None
- **Status:** ⏸️ Not Started
- **Notes:** This service makes HTTP calls to same Functions app (circular dependency)

#### **CLEAN-002: Remove HTTP-based SignalR Services** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Functions/Services/AsyncQueuedSignalRService.cs` and related
- **Time:** 20 minutes
- **Description:** Remove all HTTP-based SignalR service implementations
- **Acceptance Criteria:**
  - [ ] Delete AsyncQueuedSignalRService.cs
  - [ ] Delete any HTTP-based SignalR client implementations
  - [ ] Remove HTTP SignalR configurations from appsettings
  - [ ] Clean up BaseUrl configurations pointing to same Functions app
- **Dependencies:** None
- **Status:** ⏸️ Not Started

#### **CLEAN-003: Remove Complex SignalR Interfaces** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Core/Interfaces/` SignalR-related interfaces
- **Time:** 15 minutes
- **Description:** Remove over-engineered SignalR interfaces
- **Acceptance Criteria:**
  - [ ] Remove IEnhancedMigrationSignalRService interface
  - [ ] Remove any other complex SignalR interfaces not needed
  - [ ] Keep only basic IMigrationSignalRService if it's simple
  - [ ] Update references to removed interfaces
- **Dependencies:** CLEAN-001
- **Status:** ⏸️ Not Started

#### **CLEAN-004: Audit and Remove SignalR Helper Classes** ⏸️ Pending
- **File:** Various locations with SignalR helpers
- **Time:** 20 minutes
- **Description:** Find and remove any SignalR helper/utility classes
- **Acceptance Criteria:**
  - [ ] Search for SignalR-related helper classes
  - [ ] Remove connection managers, message formatters, etc.
  - [ ] Remove any custom SignalR middleware not needed
  - [ ] Document what was removed in notes
- **Dependencies:** None
- **Status:** ⏸️ Not Started

### **Stage 0.2: Clean Service Registration & Configuration (4 tasks)**

#### **CLEAN-005: Remove Complex SignalR Service Registration** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Functions/Extensions/ServiceCollectionExtensions.cs`
- **Time:** 25 minutes
- **Description:** Clean up over-engineered SignalR service registrations
- **Acceptance Criteria:**
  - [ ] Remove RegisterOptimizedSignalRService method
  - [ ] Remove complex SignalR strategy pattern registrations
  - [ ] Remove HTTP-based SignalR client registrations
  - [ ] Keep only basic Azure SignalR service registration
  - [ ] Simplify to use standard Azure Functions SignalR approach
- **Dependencies:** CLEAN-001, CLEAN-002, CLEAN-003
- **Status:** ⏸️ Not Started

#### **CLEAN-006: Remove SignalR Configuration Classes** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Core/Models/` configuration models
- **Time:** 15 minutes
- **Description:** Remove complex SignalR configuration models
- **Acceptance Criteria:**
  - [ ] Remove SignalRConfiguration class if over-complex
  - [ ] Remove HTTP-based SignalR settings
  - [ ] Keep only Azure SignalR connection string configuration
  - [ ] Update appsettings.json to remove complex SignalR config
- **Dependencies:** CLEAN-005
- **Status:** ⏸️ Not Started

#### **CLEAN-007: Clean SignalR from Orchestrators** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Orchestration/Orchestrators/*.cs`
- **Time:** 30 minutes
- **Description:** Remove all SignalR calls from orchestrators (deterministic violations)
- **Acceptance Criteria:**
  - [ ] Remove all BroadcastProgressUpdateAsync calls
  - [ ] Remove all BroadcastStatusUpdateAsync calls
  - [ ] Remove all BroadcastDetailedProgressAsync calls
  - [ ] Remove all BroadcastBatchStartedAsync calls
  - [ ] Remove SignalR service dependencies from constructors
  - [ ] Add TODO comments where queue events will be added later
- **Dependencies:** None
- **Status:** ⏸️ Not Started
- **Notes:** **CRITICAL** - This fixes deterministic violations

#### **CLEAN-008: Update NuGet Packages** ⏸️ Pending
- **File:** `*.csproj` files
- **Time:** 20 minutes
- **Description:** Remove old SignalR packages and add correct Azure Functions SignalR package
- **Acceptance Criteria:**
  - [ ] Remove any custom/complex SignalR NuGet packages
  - [ ] Add **Microsoft.Azure.Functions.Worker.Extensions.SignalRService** (latest stable)
  - [ ] Ensure Azure Functions Worker v4+ compatibility
  - [ ] Remove any HTTP client packages used for SignalR
  - [ ] Verify all projects build successfully
- **Dependencies:** CLEAN-001, CLEAN-002, CLEAN-005
- **Status:** ⏸️ Not Started
- **Notes:** Use ONLY the official Azure Functions SignalR extension

---

## 🔧 **Phase 1: Backend Implementation (18 Tasks)**

### **Stage 1.1: Core Models & Interfaces (4 tasks)**

#### **BACK-001-01: Create ProgressEvent Model** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Core/Models/ProgressEvent.cs`
- **Time:** 30 minutes
- **Description:** Create the core progress event model for queue messages
- **Acceptance Criteria:**
  - [ ] Class has all required properties (MigrationId, EventType, EntityType, Timestamp, EventData, Metadata)
  - [ ] Properties have appropriate data types and default values
  - [ ] Class is serializable for queue storage
  - [ ] Documentation comments added
- **Dependencies:** CLEAN-007 (Orchestrators cleaned)
- **Status:** ⏸️ Not Started
- **Notes:** Foundation model for all queue-based events

#### **BACK-001-02: Create Queue Message Extensions** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Core/Extensions/ProgressEventExtensions.cs`
- **Time:** 20 minutes
- **Description:** Helper methods for ProgressEvent manipulation
- **Acceptance Criteria:**
  - [ ] ToJson() method for queue serialization
  - [ ] FromJson() method for queue deserialization  
  - [ ] CreateProgressEvent() factory methods
  - [ ] Validation methods (IsValid(), HasRequiredFields())
- **Dependencies:** BACK-001-01
- **Status:** ⏸️ Not Started

#### **BACK-001-03: Update Core Interfaces** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Core/Interfaces/IQueueProgressService.cs`
- **Time:** 15 minutes
- **Description:** Add queue service interface
- **Acceptance Criteria:**
  - [ ] IQueueProgressService interface created
  - [ ] Methods: QueueProgressEvent, QueueBatchEvent, QueueStatusEvent
  - [ ] Async/await support
  - [ ] CancellationToken support
- **Dependencies:** BACK-001-01, CLEAN-003 (Interfaces cleaned)
- **Status:** ⏸️ Not Started

#### **BACK-001-04: Add Queue Configuration Model** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Core/Models/QueueConfiguration.cs`
- **Time:** 15 minutes
- **Description:** Configuration for queue settings
- **Acceptance Criteria:**
  - [ ] Queue names (progress-updates, batch-events, status-updates)
  - [ ] Retry policies and timeouts
  - [ ] Dead letter queue settings
  - [ ] Validation attributes
- **Dependencies:** CLEAN-006 (Configuration cleaned)
- **Status:** ⏸️ Not Started

### **Stage 1.2: Activity Functions (5 tasks)**

#### **BACK-002-01: Create QueueProgressEvent Activity** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Orchestration/Activities/QueueProgressEventActivity.cs`
- **Time:** 45 minutes
- **Description:** Activity function to queue progress events
- **Acceptance Criteria:**
  - [ ] [FunctionName("QueueProgressEvent")] attribute
  - [ ] [ActivityTrigger] parameter binding
  - [ ] [Queue("progress-updates")] output binding
  - [ ] Error handling with try-catch
  - [ ] Returns success/failure boolean
  - [ ] Logging for debugging
- **Dependencies:** BACK-001-01, BACK-001-03
- **Status:** ⏸️ Not Started

#### **BACK-002-02: Create QueueBatchEvent Activity** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Orchestration/Activities/QueueBatchEventActivity.cs`
- **Time:** 30 minutes
- **Description:** Activity function for batch-specific events
- **Acceptance Criteria:**
  - [ ] Handles BatchStarted, BatchProgress, BatchCompleted events
  - [ ] Separate queue binding [Queue("batch-events")]
  - [ ] Batch-specific metadata included
  - [ ] Non-blocking error handling
- **Dependencies:** BACK-002-01
- **Status:** ⏸️ Not Started

#### **BACK-002-03: Create QueueStatusEvent Activity** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Orchestration/Activities/QueueStatusEventActivity.cs`
- **Time:** 30 minutes
- **Description:** Activity function for migration status changes
- **Acceptance Criteria:**
  - [ ] Handles migration started, completed, failed, cancelled
  - [ ] [Queue("status-updates")] binding
  - [ ] Status-specific event data formatting
  - [ ] Integration with existing status models
- **Dependencies:** BACK-002-01
- **Status:** ⏸️ Not Started

#### **BACK-002-04: Add Activity Error Handling** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Orchestration/Activities/QueueActivityErrorHandler.cs`
- **Time:** 30 minutes
- **Description:** Centralized error handling for queue activities
- **Acceptance Criteria:**
  - [ ] Common error handling patterns
  - [ ] Dead letter queue fallback
  - [ ] Error telemetry/logging
  - [ ] Graceful degradation strategies
- **Dependencies:** BACK-002-01, BACK-002-02, BACK-002-03
- **Status:** ⏸️ Not Started

#### **BACK-002-05: Update Activity Registration** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Orchestration/Extensions/ServiceCollectionExtensions.cs`
- **Time:** 15 minutes
- **Description:** Register new queue activities
- **Acceptance Criteria:**
  - [ ] Register IQueueProgressService implementation
  - [ ] Add queue configuration binding
  - [ ] Update dependency injection
  - [ ] Maintain existing registrations
- **Dependencies:** BACK-001-03, BACK-002-01, CLEAN-005 (Service registration cleaned)
- **Status:** ⏸️ Not Started

### **Stage 1.3: Simple SignalR Queue Processors (4 tasks)**

#### **BACK-003-01: Create Simple Progress Queue Processor** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Functions/Functions/ProgressQueueProcessor.cs`
- **Time:** 45 minutes
- **Description:** **SIMPLE** function to process progress update queue with Azure Functions SignalR
- **Acceptance Criteria:**
  - [ ] [QueueTrigger("progress-updates")] binding
  - [ ] **Direct [SignalROutput] binding** (no HTTP calls)
  - [ ] SignalR group targeting ($"migration-{migrationId}")
  - [ ] Event type routing (progress, detailed, context)
  - [ ] Simple error handling with logging
  - [ ] Dead letter queue on failure
- **Dependencies:** BACK-001-01, BACK-002-01, CLEAN-008 (Correct NuGet packages)
- **Status:** ⏸️ Not Started
- **Notes:** **Use Azure Functions SignalR bindings directly - NO HTTP calls**

#### **BACK-003-02: Create Simple Batch Queue Processor** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Functions/Functions/BatchQueueProcessor.cs`
- **Time:** 30 minutes
- **Description:** **SIMPLE** function to process batch event queue
- **Acceptance Criteria:**
  - [ ] [QueueTrigger("batch-events")] binding  
  - [ ] **Direct [SignalROutput] binding**
  - [ ] Batch event type handling (started, progress, completed)
  - [ ] SignalR broadcasting to migration groups
  - [ ] Performance metrics integration
- **Dependencies:** BACK-002-02, BACK-003-01
- **Status:** ⏸️ Not Started

#### **BACK-003-03: Create Simple Status Queue Processor** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Functions/Functions/StatusQueueProcessor.cs`
- **Time:** 30 minutes
- **Description:** **SIMPLE** function to process migration status queue
- **Acceptance Criteria:**
  - [ ] [QueueTrigger("status-updates")] binding
  - [ ] **Direct [SignalROutput] binding**
  - [ ] Status change broadcasting (started, completed, failed, cancelled)
  - [ ] Notification service integration
  - [ ] Migration completion handling
- **Dependencies:** BACK-002-03, BACK-003-01
- **Status:** ⏸️ Not Started

#### **BACK-003-04: Simple SignalR Service Registration** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Functions/Extensions/ServiceCollectionExtensions.cs`
- **Time:** 15 minutes
- **Description:** **SIMPLE** Azure Functions SignalR service registration
- **Acceptance Criteria:**
  - [ ] Use **builder.Services.AddSignalR()** (Azure Functions standard)
  - [ ] Configure Azure SignalR connection string only
  - [ ] Remove all complex strategy patterns
  - [ ] Simple, clean registration
- **Dependencies:** BACK-003-01, BACK-003-02, BACK-003-03, CLEAN-005 (Complex registration removed)
- **Status:** ⏸️ Not Started
- **Notes:** **Keep it simple - use standard Azure Functions SignalR approach**

### **Stage 1.4: Orchestrator Updates (5 tasks)**

#### **BACK-004-01: Add Queue Events to MigrationOrchestrator** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Orchestration/Orchestrators/MigrationOrchestrator.cs`
- **Time:** 45 minutes
- **Description:** Replace removed SignalR calls with queue events
- **Acceptance Criteria:**
  - [ ] Replace TODO comments with QueueProgressEvent calls
  - [ ] Replace removed BroadcastStatusUpdateAsync with QueueStatusEvent
  - [ ] Maintain existing orchestrator logic
  - [ ] Add error handling for queue failures (non-blocking)
- **Dependencies:** BACK-002-01, BACK-002-03, CLEAN-007 (SignalR calls removed)
- **Status:** ⏸️ Not Started

#### **BACK-004-02: Add Queue Events to EntityMigrationOrchestrator** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Orchestration/Orchestrators/EntityMigrationOrchestrator.cs`
- **Time:** 60 minutes
- **Description:** Replace removed SignalR calls with queue events
- **Acceptance Criteria:**
  - [ ] Replace TODO comments with QueueProgressEvent calls
  - [ ] Replace removed BroadcastBatchStartedAsync with QueueBatchEvent
  - [ ] Replace removed BroadcastBatchCompletedAsync with QueueBatchEvent
  - [ ] Update progress tracking logic
  - [ ] Maintain deterministic behavior
- **Dependencies:** BACK-002-01, BACK-002-02, CLEAN-007 (SignalR calls removed)
- **Status:** ⏸️ Not Started

#### **BACK-004-03: Update Activity Functions** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Orchestration/Activities/*`
- **Time:** 45 minutes
- **Description:** Remove any remaining SignalR calls from activities
- **Acceptance Criteria:**
  - [ ] Remove SignalR calls from ProcessEntityBatchActivity
  - [ ] Remove SignalR calls from BroadcastProgressActivity (or delete if not needed)
  - [ ] Remove SignalR calls from BroadcastMigrationEventActivity (or delete if not needed)
  - [ ] Update activity error handling
- **Dependencies:** BACK-004-01, BACK-004-02
- **Status:** ⏸️ Not Started

#### **BACK-004-04: Clean Up SignalR Dependencies** ⏸️ Pending
- **File:** All orchestration files
- **Time:** 20 minutes
- **Description:** Remove remaining SignalR service dependencies
- **Acceptance Criteria:**
  - [ ] Remove SignalR service constructor parameters
  - [ ] Remove SignalR using statements
  - [ ] Clean up any remaining SignalR references
  - [ ] Verify no compilation errors
- **Dependencies:** BACK-004-01, BACK-004-02, BACK-004-03
- **Status:** ⏸️ Not Started

#### **BACK-004-05: Validate Deterministic Behavior** ⏸️ Pending
- **File:** All orchestrator files
- **Time:** 30 minutes
- **Description:** Ensure no non-deterministic operations remain
- **Acceptance Criteria:**
  - [ ] No direct HTTP calls in orchestrators
  - [ ] No SignalR service calls in orchestrators
  - [ ] Only activity calls for external operations
  - [ ] Replay-safe operation ordering
  - [ ] Static analysis validation passes
- **Dependencies:** BACK-004-04
- **Status:** ⏸️ Not Started

---

## 📱 **Phase 2: Frontend Enhancement (12 Tasks)**
*[Frontend tasks remain the same as before - no changes needed]*

### **Stage 2.1: Connection Management (4 tasks)**

#### **FRONT-001: Already Complete** ✅ Complete
- **Description:** Current SignalR service already handles all event types correctly
- **Status:** ✅ Complete
- **Notes:** No changes needed - existing implementation works perfectly

#### **FRONT-002-01: Enhance Connection State** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Dashboard/src/types/index.ts`
- **Time:** 20 minutes
- **Description:** Add queue-aware connection state types
- **Acceptance Criteria:**
  - [ ] QueueAwareConnectionState interface
  - [ ] queueBacklog: number property
  - [ ] lastEventTimestamp: Date property
  - [ ] isReceivingQueueUpdates: boolean property
- **Dependencies:** None
- **Status:** ⏸️ Not Started

#### **FRONT-002-02: Update SignalR Service State** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Dashboard/src/services/signalRService.ts`
- **Time:** 30 minutes
- **Description:** Enhance connection state management
- **Acceptance Criteria:**
  - [ ] Track queue backlog estimation
  - [ ] Monitor last event timestamps
  - [ ] Detect queue update patterns
  - [ ] Enhanced reconnection logic
- **Dependencies:** FRONT-002-01
- **Status:** ⏸️ Not Started

#### **FRONT-002-03: Auto-Reconnect Group Joining** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Dashboard/src/services/signalRService.ts`
- **Time:** 25 minutes
- **Description:** Automatically rejoin migration groups on reconnect
- **Acceptance Criteria:**
  - [ ] Store active migration subscriptions
  - [ ] Auto-rejoin groups after reconnection
  - [ ] Handle connection state transitions
  - [ ] Error recovery for group joining
- **Dependencies:** FRONT-002-02
- **Status:** ⏸️ Not Started

### **Stage 2.2: Enhanced Fallback Polling (4 tasks)**

#### **FRONT-003-01: Create Adaptive Polling Config** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Dashboard/src/config/pollingConfig.ts`
- **Time:** 15 minutes
- **Description:** Configuration for adaptive polling intervals
- **Acceptance Criteria:**
  - [ ] normalInterval: 30000 (30s when queue working)
  - [ ] fastInterval: 5000 (5s when queue issues detected)
  - [ ] noEventThreshold: 60000 (trigger fast polling)
  - [ ] maxFastPollingDuration: 300000 (5 min max)
- **Dependencies:** None
- **Status:** ⏸️ Not Started

#### **FRONT-003-02: Implement Queue Activity Detection** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Dashboard/src/hooks/useQueueActivityDetection.ts`
- **Time:** 35 minutes
- **Description:** Hook to detect queue activity patterns
- **Acceptance Criteria:**
  - [ ] Monitor event frequency patterns
  - [ ] Detect when no events received for threshold
  - [ ] Switch between normal/fast polling modes
  - [ ] Return isQueueActive boolean state
- **Dependencies:** FRONT-003-01
- **Status:** ⏸️ Not Started

#### **FRONT-003-03: Update Real-time Progress Hook** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Dashboard/src/hooks/useRealTimeMigrationProgress.ts`
- **Time:** 30 minutes
- **Description:** Integrate adaptive polling
- **Acceptance Criteria:**
  - [ ] Use useQueueActivityDetection hook
  - [ ] Switch polling intervals based on queue activity
  - [ ] Maintain existing polling logic
  - [ ] Enhanced error recovery
- **Dependencies:** FRONT-003-02
- **Status:** ⏸️ Not Started

#### **FRONT-003-04: Update Detailed Progress Hook** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Dashboard/src/hooks/useDetailedMigrationProgress.ts`
- **Time:** 25 minutes
- **Description:** Integrate adaptive polling
- **Acceptance Criteria:**
  - [ ] Same adaptive polling integration
  - [ ] Maintain detailed event handling
  - [ ] Queue-aware state management
  - [ ] Backward compatibility
- **Dependencies:** FRONT-003-02
- **Status:** ⏸️ Not Started

### **Stage 2.3: UI Enhancements (4 tasks)**

#### **FRONT-004: Already Complete** ✅ Complete
- **Description:** Current dashboard already displays all required information
- **Status:** ✅ Complete
- **Notes:** No UI changes needed - existing components handle everything

#### **FRONT-005-01: Add Queue Status Indicator** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Dashboard/src/components/Dashboard/ConnectionStatusIndicator.tsx`
- **Time:** 30 minutes
- **Description:** Visual indicator for queue-based updates
- **Acceptance Criteria:**
  - [ ] Queue activity status display
  - [ ] Event frequency indicator
  - [ ] Fallback polling mode indicator
  - [ ] Connection health visualization
- **Dependencies:** FRONT-002-02
- **Status:** ⏸️ Not Started

#### **FRONT-005-02: Enhance Error Handling** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Dashboard/src/components/Notifications/ErrorBoundary.tsx`
- **Time:** 25 minutes
- **Description:** Queue-aware error handling
- **Acceptance Criteria:**
  - [ ] Queue connection error handling
  - [ ] Graceful degradation messaging
  - [ ] Recovery action suggestions
  - [ ] Enhanced error context
- **Dependencies:** FRONT-002-02
- **Status:** ⏸️ Not Started

#### **FRONT-005-03: Update Dashboard Context** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Dashboard/src/context/DashboardContext.tsx`
- **Time:** 20 minutes
- **Description:** Integrate queue-aware state management
- **Acceptance Criteria:**
  - [ ] Include queue connection state
  - [ ] Track active migration subscriptions
  - [ ] Enhanced error state management
  - [ ] Backward compatibility maintained
- **Dependencies:** FRONT-002-01, FRONT-002-02
- **Status:** ⏸️ Not Started

---

## 🧪 **Phase 3: Testing & Validation (10 Tasks)**
*[Testing tasks remain the same - updated dependencies only]*

### **Stage 3.1: Unit Tests (4 tasks)**

#### **TEST-001-01: Queue Activity Unit Tests** ⏸️ Pending
- **File:** `tests/BigCommerce.Migration.UnitTests/Orchestration/Activities/QueueProgressEventActivityTests.cs`
- **Time:** 45 minutes
- **Description:** Test queue activity functions
- **Acceptance Criteria:**
  - [ ] Test successful event queuing
  - [ ] Test error handling scenarios
  - [ ] Test serialization/deserialization
  - [ ] Test return value handling
- **Dependencies:** BACK-002-01
- **Status:** ⏸️ Not Started

#### **TEST-001-02: Queue Processor Unit Tests** ⏸️ Pending
- **File:** `tests/BigCommerce.Migration.UnitTests/Functions/QueueProcessorTests.cs`
- **Time:** 60 minutes
- **Description:** Test queue processor functions
- **Acceptance Criteria:**
  - [ ] Test message processing logic
  - [ ] Test SignalR group targeting
  - [ ] Test error handling and dead letter queue
  - [ ] Test event type routing
- **Dependencies:** BACK-003-01
- **Status:** ⏸️ Not Started

#### **TEST-001-03: Frontend Hook Unit Tests** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Dashboard/src/hooks/__tests__/useQueueActivityDetection.test.ts`
- **Time:** 40 minutes
- **Description:** Test queue activity detection logic
- **Acceptance Criteria:**
  - [ ] Test queue activity pattern recognition
  - [ ] Test polling interval switching
  - [ ] Test threshold detection
  - [ ] Test error scenarios
- **Dependencies:** FRONT-003-02
- **Status:** ⏸️ Not Started

#### **TEST-001-04: SignalR Service Unit Tests** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Dashboard/src/services/__tests__/signalRService.test.ts`
- **Time:** 35 minutes
- **Description:** Test enhanced SignalR service features
- **Acceptance Criteria:**
  - [ ] Test auto-reconnect group joining
  - [ ] Test queue-aware state management
  - [ ] Test connection state transitions
  - [ ] Test error recovery scenarios
- **Dependencies:** FRONT-002-03
- **Status:** ⏸️ Not Started

### **Stage 3.2: Integration Tests (3 tasks)**

#### **TEST-002-01: Queue to SignalR Flow Test** ⏸️ Pending
- **File:** `tests/BigCommerce.Migration.IntegrationTests/QueueSignalRFlowTests.cs`
- **Time:** 60 minutes
- **Description:** End-to-end queue to SignalR flow testing
- **Acceptance Criteria:**
  - [ ] Test orchestrator → queue → processor → SignalR flow
  - [ ] Test group targeting accuracy
  - [ ] Test message ordering and timing
  - [ ] Test error recovery and retries
- **Dependencies:** BACK-003-01, BACK-004-01
- **Status:** ⏸️ Not Started

#### **TEST-002-02: Scaling Behavior Test** ⏸️ Pending
- **File:** `tests/BigCommerce.Migration.IntegrationTests/QueueScalingTests.cs`
- **Time:** 45 minutes
- **Description:** Test queue scaling and deduplication
- **Acceptance Criteria:**
  - [ ] Test multiple function instances
  - [ ] Verify no duplicate messages
  - [ ] Test queue depth scaling triggers
  - [ ] Validate client targeting precision
- **Dependencies:** TEST-002-01
- **Status:** ⏸️ Not Started

#### **TEST-002-03: Frontend Integration Test** ⏸️ Pending
- **File:** `src/BigCommerce.Migration.Dashboard/src/components/Tests/QueueIntegrationTest.tsx`
- **Time:** 50 minutes
- **Description:** Test frontend queue integration
- **Acceptance Criteria:**
  - [ ] Test adaptive polling behavior
  - [ ] Test auto-reconnect functionality
  - [ ] Test group joining/leaving
  - [ ] Test fallback scenarios
- **Dependencies:** FRONT-003-03, FRONT-002-03
- **Status:** ⏸️ Not Started

### **Stage 3.3: End-to-End Tests (3 tasks)**

#### **TEST-003-01: Deterministic Behavior Test** ⏸️ Pending
- **File:** `tests/BigCommerce.Migration.IntegrationTests/DeterministicBehaviorTests.cs`
- **Time:** 30 minutes
- **Description:** Validate orchestrator deterministic behavior
- **Acceptance Criteria:**
  - [ ] Test orchestrator replay scenarios
  - [ ] Verify no external I/O in orchestrators
  - [ ] Test deterministic execution paths
  - [ ] Validate static analysis compliance
- **Dependencies:** BACK-004-05
- **Status:** ⏸️ Not Started

#### **TEST-003-02: Resilience Testing** ⏸️ Pending
- **File:** `tests/BigCommerce.Migration.IntegrationTests/SignalRResilienceTests.cs`
- **Time:** 45 minutes
- **Description:** Test SignalR failure scenarios
- **Acceptance Criteria:**
  - [ ] Test SignalR service downtime
  - [ ] Test queue service failures
  - [ ] Test network connectivity issues
  - [ ] Verify graceful degradation
- **Dependencies:** TEST-002-01
- **Status:** ⏸️ Not Started

#### **TEST-003-03: Full Migration Test** ⏸️ Pending
- **File:** `tests/BigCommerce.Migration.IntegrationTests/FullMigrationQueueTests.cs`
- **Time:** 60 minutes
- **Description:** Complete migration with queue-based updates
- **Acceptance Criteria:**
  - [ ] Run full migration end-to-end
  - [ ] Verify all progress updates received
  - [ ] Test multiple concurrent migrations
  - [ ] Validate performance metrics
- **Dependencies:** All previous tests
- **Status:** ⏸️ Not Started

---

## 📚 **Phase 4: Documentation & Deployment (5 Tasks)**
*[Documentation tasks remain the same]*

#### **DOC-001: Update Architecture Documentation** ⏸️ Pending
- **File:** `docs/Architecture-Documentation.md`
- **Time:** 45 minutes
- **Description:** Document new queue-based architecture
- **Acceptance Criteria:**
  - [ ] Update architecture diagrams
  - [ ] Document queue message flows
  - [ ] Update component interaction diagrams
  - [ ] Add troubleshooting guide
- **Dependencies:** All implementation phases
- **Status:** ⏸️ Not Started

#### **DOC-002: Create Queue Configuration Guide** ⏸️ Pending
- **File:** `docs/Queue-Configuration-Guide.md`
- **Time:** 30 minutes
- **Description:** Configuration guide for queue settings
- **Acceptance Criteria:**
  - [ ] Queue connection string setup
  - [ ] Retry policy configuration
  - [ ] Dead letter queue setup
  - [ ] Monitoring and alerting setup
- **Dependencies:** BACK-001-04, BACK-003-04
- **Status:** ⏸️ Not Started

#### **DOC-003: Update API Documentation** ⏸️ Pending
- **File:** `docs/API-Documentation.md`
- **Time:** 25 minutes
- **Description:** Document new queue event types
- **Acceptance Criteria:**
  - [ ] Document ProgressEvent schema
  - [ ] Update SignalR event documentation
  - [ ] Add queue configuration endpoints
  - [ ] Update troubleshooting examples
- **Dependencies:** BACK-001-01, BACK-003-01
- **Status:** ⏸️ Not Started

#### **DOC-004: Create Deployment Checklist** ⏸️ Pending
- **File:** `docs/Queue-Deployment-Checklist.md`
- **Time:** 20 minutes
- **Description:** Step-by-step deployment guide
- **Acceptance Criteria:**
  - [ ] Pre-deployment verification steps
  - [ ] Queue infrastructure setup
  - [ ] Function deployment order
  - [ ] Post-deployment validation
- **Dependencies:** All phases
- **Status:** ⏸️ Not Started

#### **DOC-005: Update Status Tracker** ⏸️ Pending
- **File:** `docs/SignalR-Implementation-Status-Tracker.md`
- **Time:** 15 minutes
- **Description:** Final status update
- **Acceptance Criteria:**
  - [ ] Mark all tasks as complete
  - [ ] Update progress percentages
  - [ ] Add final implementation notes
  - [ ] Document lessons learned
- **Dependencies:** All previous tasks
- **Status:** ⏸️ Not Started

---

## 📈 **Progress Tracking Guidelines**

### **Task Status Updates**
- **⏸️ Pending** → Change when starting work
- **🔄 In Progress** → Change when task started
- **✅ Complete** → Change when task finished and tested
- **❌ Blocked** → Change when dependency issues arise
- **⚠️ At Risk** → Change when delays detected

### **Daily Update Process**
1. **Mark current task as 🔄 In Progress**
2. **Update time estimates** if needed
3. **Add notes** about issues or discoveries
4. **Update progress percentages** in overview table
5. **Mark completed tasks as ✅ Complete**

### **Completion Criteria**
Each task is complete when:
- [ ] All acceptance criteria met
- [ ] Code reviewed (if applicable)
- [ ] Unit tests passing (if applicable)
- [ ] Documentation updated (if applicable)
- [ ] Integration tested (if applicable)

---

## 🚀 **Next Steps for Implementation**

### **✅ MUST START WITH Phase 0 (Cleanup):**
1. **CLEAN-001**: Remove EnhancedMigrationSignalRService (15 min)
2. **CLEAN-002**: Remove HTTP-based SignalR Services (20 min)  
3. **CLEAN-007**: Clean SignalR from Orchestrators (30 min) **CRITICAL**
4. **CLEAN-008**: Update NuGet Packages (20 min)

**Total Phase 0 Time: ~2.5 hours**

### **🎯 Current Priority: CLEAN-007**
**MOST CRITICAL:** Remove SignalR calls from orchestrators to fix deterministic violations immediately.

### **Correct NuGet Package:**
**Microsoft.Azure.Functions.Worker.Extensions.SignalRService** (latest stable)
- No custom HTTP implementations
- No over-engineering
- Standard Azure Functions approach

---

**Document Updated:** January 2025  
**Next Update:** After completion of Phase 0 cleanup  
**Total Estimated Time:** ~50 hours across all phases (including cleanup) 