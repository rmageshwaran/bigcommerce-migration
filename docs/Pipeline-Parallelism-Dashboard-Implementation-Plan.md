# Pipeline Parallelism Dashboard Implementation Plan

## 🎯 **PROJECT OVERVIEW**

**Project**: Comprehensive Entity Migration Progress Dashboard  
**Goal**: Create a new dashboard page that displays real-time pipeline parallelism progress as shown in the attached screenshot  
**Current Status**: 🔴 **NOT STARTED** - Ready for planning and implementation  
**Priority**: **HIGH** - Required for Enhanced Product Migration Phase 2  
**Estimated Effort**: 24 hours total

---

## 🖼️ **TARGET DESIGN - Analysis from Screenshot**

### **Main Display Elements**
1. **Header**: "📊 Comprehensive Entity Migration Progress: 71.3% Complete"
2. **Overall Progress Bar**: Visual progress indicator with 856/1,200 entities
3. **Entity Breakdown Section**: 4 individual progress bars
   - Options: 200/200 (100%) ✅ 15.2/sec
   - Modifiers: 150/180 (83%) 🔄 8.7/sec  
   - Images: 320/520 (62%) 🔄 12.1/sec
   - Reviews: 186/200 (93%) 🔄 9.3/sec
4. **Status Footer**: "Processing • Elapsed: 2m 34s • ETA: 1m 12s"

### **Key UI Features Needed**
- **Progress Indicators**: Different icons (✅ ❌ 🔄) for status
- **Real-time Throughput**: Entities per second for each type
- **Combined Progress Calculation**: Overall percentage across all entity types
- **Time Tracking**: Elapsed time and estimated completion
- **Live Updates**: SignalR integration for real-time data

---

## 🏗️ **ARCHITECTURE OVERVIEW**

### **Component Hierarchy**
```
ComprehensiveEntityMigrationPage
├── ComprehensiveProgressHeader
├── OverallProgressSection  
├── EntityBreakdownGrid
│   ├── EntityProgressCard (Options)
│   ├── EntityProgressCard (Modifiers)
│   ├── EntityProgressCard (Images)
│   └── EntityProgressCard (Reviews)
└── MigrationStatusFooter
```

### **Data Flow**
```
Pipeline Processing → SignalR Events → Dashboard Components → Real-time UI Updates
```

---

## 📊 **NEW SIGNALR EVENTS REQUIRED**

### **1. ComprehensiveEntityProgressEvent**
```csharp
public class ComprehensiveEntityProgressEvent : ProgressEvent
{
    public string EntityType { get; set; } // "options", "modifiers", "images", "reviews"
    public int TotalCount { get; set; }
    public int ProcessedCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double ProgressPercentage { get; set; }
    public double ThroughputPerSecond { get; set; }
    public string Status { get; set; } // "pending", "processing", "completed", "failed"
    public TimeSpan? ProcessingTime { get; set; }
}
```

### **2. OverallPipelineProgressEvent**
```csharp
public class OverallPipelineProgressEvent : ProgressEvent
{
    public double OverallProgressPercentage { get; set; }
    public int TotalEntitiesAllTypes { get; set; }
    public int ProcessedEntitiesAllTypes { get; set; }
    public Dictionary<string, EntityTypeProgress> EntityBreakdown { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }
    public string CurrentPhase { get; set; } // "Phase 2: Comprehensive Entity Migration"
}
```

### **3. PipelineThroughputEvent**
```csharp
public class PipelineThroughputEvent : ProgressEvent
{
    public Dictionary<string, double> EntityTypeThroughput { get; set; }
    public double OverallThroughput { get; set; }
    public int ActiveChannels { get; set; }
}
```

---

## 🚀 **IMPLEMENTATION TASK BREAKDOWN**

### **Phase 1: Backend Infrastructure (8 hours)**

#### **P1-T1: Create New SignalR Event Models (2 hours)**
- **Files to Create:**
  - `src/BigCommerce.Migration.Core/Models/ComprehensiveEntityProgressEvent.cs`
  - `src/BigCommerce.Migration.Core/Models/OverallPipelineProgressEvent.cs`
  - `src/BigCommerce.Migration.Core/Models/PipelineThroughputEvent.cs`
- **Tasks:**
  - Add new event types to ProgressEvent.cs JsonDerivedType attributes
  - Implement comprehensive entity progress tracking models
  - Add pipeline-specific progress calculation logic

#### **P1-T2: Extend SignalREventFactory (2 hours)**
- **Files to Modify:**
  - `src/BigCommerce.Migration.Core/Services/SignalREventFactory.cs`
  - `src/BigCommerce.Migration.Core/Models/SignalREventOptions.cs`
- **Tasks:**
  - Add CreateComprehensiveEntityProgress method
  - Add CreateOverallPipelineProgress method
  - Add CreatePipelineThroughput method
  - Create corresponding options classes

#### **P1-T3: Create PipelineProgressAggregator (4 hours)**
- **Files to Create:**
  - `src/BigCommerce.Migration.Infrastructure/Services/PipelineProgressAggregator.cs`
- **Tasks:**
  - Implement thread-safe multi-channel progress aggregation
  - Add real-time throughput calculation (entities/second)
  - Implement ETA calculation based on current throughput
  - Add SignalR event broadcasting integration

### **Phase 2: Frontend Components (12 hours)**

#### **P2-T1: Create Main Page Component (3 hours)**
- **Files to Create:**
  - `src/BigCommerce.Migration.Dashboard/src/components/Views/ComprehensiveEntityMigrationPage.tsx`
- **Tasks:**
  - Create main page layout matching screenshot design
  - Implement routing for new page
  - Add navigation integration

#### **P2-T2: Create Progress Components (4 hours)**
- **Files to Create:**
  - `src/BigCommerce.Migration.Dashboard/src/components/Progress/ComprehensiveProgressHeader.tsx`
  - `src/BigCommerce.Migration.Dashboard/src/components/Progress/OverallProgressSection.tsx`
  - `src/BigCommerce.Migration.Dashboard/src/components/Progress/EntityBreakdownGrid.tsx`
  - `src/BigCommerce.Migration.Dashboard/src/components/Progress/EntityProgressCard.tsx`
  - `src/BigCommerce.Migration.Dashboard/src/components/Progress/MigrationStatusFooter.tsx`
- **Tasks:**
  - Implement progress bars with percentage display
  - Add throughput display (entities/sec)
  - Add status icons (✅ ❌ 🔄)
  - Implement responsive grid layout

#### **P2-T3: Create SignalR Hook (3 hours)**
- **Files to Create:**
  - `src/BigCommerce.Migration.Dashboard/src/hooks/useComprehensiveEntityProgress.ts`
- **Tasks:**
  - Implement SignalR connection for comprehensive entity events
  - Add real-time data aggregation
  - Implement connection state management
  - Add error handling and reconnection logic

#### **P2-T4: Update Type Definitions (2 hours)**
- **Files to Modify:**
  - `src/BigCommerce.Migration.Dashboard/src/types/index.ts`
- **Tasks:**
  - Add comprehensive entity progress types
  - Add pipeline progress interfaces
  - Update existing interfaces for compatibility

### **Phase 3: Integration & Testing (4 hours)**

#### **P3-T1: Integration Testing (2 hours)**
- **Files to Create:**
  - `src/BigCommerce.Migration.Dashboard/src/components/Tests/ComprehensiveEntityProgressTest.tsx`
- **Tasks:**
  - Create mock data for testing
  - Implement comprehensive progress scenarios
  - Test SignalR event handling

#### **P3-T2: Navigation Integration (2 hours)**
- **Files to Modify:**
  - `src/BigCommerce.Migration.Dashboard/src/App.tsx`
  - `src/BigCommerce.Migration.Dashboard/src/components/Layout/DashboardSidebar.tsx`
- **Tasks:**
  - Add route for comprehensive entity migration page
  - Add navigation menu item
  - Update routing configuration

---

## 🎨 **UI SPECIFICATIONS**

### **Color Scheme & Icons**
```typescript
const EntityStatusConfig = {
  completed: { icon: '✅', color: '#4caf50', label: 'Completed' },
  processing: { icon: '🔄', color: '#2196f3', label: 'Processing' },
  failed: { icon: '❌', color: '#f44336', label: 'Failed' },
  pending: { icon: '⏳', color: '#ff9800', label: 'Pending' }
};
```

### **Typography & Spacing**
- **Header**: Typography variant="h4" with custom styling
- **Progress Bars**: LinearProgress with custom thickness
- **Entity Cards**: Card components with consistent padding
- **Grid Layout**: Responsive grid (xs=12, sm=6, md=3)

### **Progress Bar Design**
```typescript
const ProgressBarConfig = {
  height: 8,
  borderRadius: 4,
  backgroundColor: theme.palette.grey[200],
  bufferColor: theme.palette.grey[300]
};
```

---

## 🔗 **API INTEGRATION POINTS**

### **New API Endpoints Required**
1. **GET** `/api/migrations/{migrationId}/comprehensive-progress`
   - Returns current comprehensive entity progress
   - Includes all entity types and their status

2. **WebSocket** Connection: `/comprehensiveProgressHub`
   - Subscribes to real-time comprehensive progress updates
   - Handles pipeline parallelism events

### **Event Subscription Pattern**
```typescript
// Subscribe to comprehensive entity progress events
connection.on('ComprehensiveEntityProgressUpdated', (data) => {
  updateEntityProgress(data.entityType, data);
});

connection.on('OverallPipelineProgressUpdated', (data) => {
  updateOverallProgress(data);
});

connection.on('PipelineThroughputUpdated', (data) => {
  updateThroughputMetrics(data);
});
```

---

## 📱 **RESPONSIVE DESIGN**

### **Mobile Layout (xs)**
- Stack entity cards vertically
- Reduce text size for throughput
- Simplified progress indicators

### **Tablet Layout (sm/md)**
- 2x2 grid for entity cards
- Maintain all information visibility
- Adjust spacing for touch interfaces

### **Desktop Layout (lg/xl)**
- 4-column grid for entity cards
- Full information display
- Enhanced visual hierarchy

---

## 🔄 **SIGNALR EVENT BROADCASTING MODIFICATIONS**

### **Current vs. New Event Structure**

#### **Current Events (Single Entity Focus)**
```csharp
// Current: One event per entity type
EntityProgressEvent productEvent = new EntityProgressEvent {
    EntityType = "products",
    ProcessedCount = 150,
    TotalCount = 200
};
```

#### **New Events (Pipeline Parallelism Focus)**
```csharp
// New: Comprehensive multi-entity awareness
OverallPipelineProgressEvent pipelineEvent = new OverallPipelineProgressEvent {
    OverallProgressPercentage = 71.3,
    TotalEntitiesAllTypes = 1200,
    ProcessedEntitiesAllTypes = 856,
    EntityBreakdown = new Dictionary<string, EntityTypeProgress> {
        ["options"] = new EntityTypeProgress { Processed = 200, Total = 200, Throughput = 15.2 },
        ["modifiers"] = new EntityTypeProgress { Processed = 150, Total = 180, Throughput = 8.7 },
        ["images"] = new EntityTypeProgress { Processed = 320, Total = 520, Throughput = 12.1 },
        ["reviews"] = new EntityTypeProgress { Processed = 186, Total = 200, Throughput = 9.3 }
    }
};
```

### **Broadcasting Frequency**
- **Individual Entity Updates**: Every 10 entities processed
- **Overall Progress Updates**: Every 5 seconds
- **Throughput Updates**: Every 3 seconds
- **Rate Limiting**: Maximum 4 events per second to prevent UI flooding

---

## ⚡ **PERFORMANCE CONSIDERATIONS**

### **SignalR Optimization**
- **Batched Updates**: Aggregate multiple entity updates into single broadcast
- **Throttling**: Prevent excessive UI updates with debouncing
- **Connection Management**: Implement heartbeat and reconnection logic

### **UI Performance**
- **Memoization**: Use React.memo for progress components
- **Virtual Scrolling**: If entity list grows beyond 10 items
- **Update Batching**: Group rapid updates to prevent render thrashing

### **Memory Management**
- **Event History Limit**: Keep only last 100 progress events
- **Cleanup**: Remove old progress data when migration completes
- **Connection Disposal**: Proper cleanup of SignalR connections

---

## 🎯 **SUCCESS CRITERIA**

### **Functional Requirements**
- ✅ Display real-time progress for all 4 entity types simultaneously
- ✅ Show individual entity throughput (entities/second)
- ✅ Calculate and display overall progress percentage
- ✅ Provide accurate time estimates (elapsed + ETA)
- ✅ Update in real-time via SignalR without page refresh

### **Performance Requirements**
- ✅ Updates display within 500ms of backend progress change
- ✅ UI remains responsive during rapid updates (>50 updates/minute)
- ✅ Memory usage stays below 50MB for 24-hour continuous operation
- ✅ No visual glitches or progress bar jumping

### **User Experience Requirements**
- ✅ Clear visual distinction between entity statuses
- ✅ Responsive design works on mobile, tablet, and desktop
- ✅ Intuitive progress representation matching user expectations
- ✅ Graceful handling of connection loss and reconnection

---

## 🚨 **RISK MITIGATION**

### **Technical Risks**
1. **SignalR Connection Overload**: Implement rate limiting and batching
2. **UI Performance Degradation**: Use React profiling and optimization
3. **Data Consistency**: Ensure aggregated progress matches individual totals
4. **Browser Compatibility**: Test across major browsers and versions

### **Timeline Risks**
1. **Backend Complexity**: Phase SignalR events implementation first
2. **Design Iteration**: Create mockups before full implementation
3. **Testing Scope**: Prioritize core functionality over edge cases
4. **Integration Issues**: Plan buffer time for SignalR debugging

---

## 📋 **IMPLEMENTATION ORDER**

### **Sprint 1 (Week 1): Backend Foundation**
1. P1-T1: Create SignalR event models
2. P1-T2: Extend SignalREventFactory
3. P1-T3: Create PipelineProgressAggregator

### **Sprint 2 (Week 2): Frontend Components**
1. P2-T1: Create main page component
2. P2-T2: Create progress components
3. P2-T4: Update type definitions

### **Sprint 3 (Week 3): Integration & Polish**
1. P2-T3: Create SignalR hook
2. P3-T1: Integration testing
3. P3-T2: Navigation integration

---

## 📖 **RELATED DOCUMENTATION**

- **Pipeline Parallelism Design**: `docs/Pipeline-Parallelism-Progress-Aggregation-Design.md`
- **Enhanced Product Migration**: `docs/Enhanced-Product-Migration-FINAL-SUMMARY.md`
- **SignalR Architecture**: `src/BigCommerce.Migration.Functions/Configuration/SignalR-Configuration-Guide.md`
- **Dashboard Integration Guide**: `src/BigCommerce.Migration.Dashboard/docs/Backend-Integration-Guide.md`