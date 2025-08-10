# Comprehensive Entity Migration Dashboard - Task Breakdown

## 🎯 **PROJECT SUMMARY**

**Project**: Comprehensive Entity Migration Dashboard Implementation  
**Goal**: Create dashboard page displaying real-time pipeline parallelism progress for Phase 2 Enhanced Product Migration  
**Status**: 🔴 **PLANNED** - Detailed task breakdown complete  
**Total Effort**: 24 hours across 3 phases  
**Dependencies**: Pipeline Parallelism core implementation must be in place

---

## 📋 **MASTER TODO LIST**

### **✅ COMPLETED**
- [x] **Pipeline-Dashboard-Planning**: Create comprehensive implementation plan and task breakdown
- [x] **Documentation-Updates**: Update Enhanced Product Migration docs with Pipeline Parallelism approach

### **🔄 PENDING TASKS**

#### **Backend Infrastructure (8 hours)**
- [ ] **signalr-events-comprehensive**: Create new SignalR events for Pipeline Parallelism progress tracking
- [ ] **pipeline-progress-aggregator**: Implement PipelineProgressAggregator for multi-channel progress coordination  
- [ ] **comprehensive-dashboard-backend**: Implement Backend Infrastructure for Comprehensive Entity Migration Dashboard

#### **Frontend Components (12 hours)**
- [ ] **comprehensive-dashboard-frontend**: Implement Frontend Components for Comprehensive Entity Migration Dashboard

#### **Integration & Testing (4 hours)**
- [ ] **comprehensive-dashboard-integration**: Integrate and Test Comprehensive Entity Migration Dashboard

#### **Core Pipeline Implementation (TBD)**
- [ ] **pipeline-parallelism-implementation**: Implement Pipeline Parallelism for Phase 2 Comprehensive Entity Migration

---

## 🚀 **DETAILED TASK BREAKDOWN**

### **PHASE 1: BACKEND INFRASTRUCTURE (8 hours)**

#### **P1-T1: Create New SignalR Event Models** ⭐  
**Priority**: Critical | **Effort**: 2 hours | **Status**: 🔴 Pending

**Overview**: Create comprehensive SignalR event models to support pipeline parallelism progress tracking with multi-entity awareness.

**Files to Create:**
```
src/BigCommerce.Migration.Core/Models/
├── ComprehensiveEntityProgressEvent.cs
├── OverallPipelineProgressEvent.cs  
├── PipelineThroughputEvent.cs
└── EntityTypeProgress.cs
```

**Sub-tasks:**
1. **P1-T1.1**: Create ComprehensiveEntityProgressEvent
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
       public DateTime? StartTime { get; set; }
   }
   ```

2. **P1-T1.2**: Create OverallPipelineProgressEvent
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
       public double OverallThroughput { get; set; }
   }
   ```

3. **P1-T1.3**: Create PipelineThroughputEvent
   ```csharp
   public class PipelineThroughputEvent : ProgressEvent
   {
       public Dictionary<string, double> EntityTypeThroughput { get; set; }
       public double OverallThroughput { get; set; }
       public int ActiveChannels { get; set; }
       public Dictionary<string, int> ChannelQueueDepth { get; set; }
   }
   ```

4. **P1-T1.4**: Update ProgressEvent.cs with new JsonDerivedType attributes
   ```csharp
   [JsonDerivedType(typeof(ComprehensiveEntityProgressEvent), "comprehensive-entity")]
   [JsonDerivedType(typeof(OverallPipelineProgressEvent), "overall-pipeline")]
   [JsonDerivedType(typeof(PipelineThroughputEvent), "pipeline-throughput")]
   ```

**Acceptance Criteria:**
- ✅ All new event types compile and serialize correctly
- ✅ Events follow existing SignalR event patterns
- ✅ JSON serialization handles all properties correctly
- ✅ HubMethod names follow consistent naming convention

---

#### **P1-T2: Extend SignalREventFactory** ⭐  
**Priority**: Critical | **Effort**: 2 hours | **Status**: 🔴 Pending

**Overview**: Extend the centralized SignalREventFactory to support comprehensive entity progress events with auto-populated base properties.

**Files to Modify:**
```
src/BigCommerce.Migration.Core/Services/SignalREventFactory.cs
src/BigCommerce.Migration.Core/Models/SignalREventOptions.cs
```

**Sub-tasks:**
1. **P1-T2.1**: Create ComprehensiveEntityProgressOptions
   ```csharp
   public class ComprehensiveEntityProgressOptions : SignalREventOptions
   {
       public string EntityType { get; set; }
       public int TotalCount { get; set; }
       public int ProcessedCount { get; set; }
       public int SuccessCount { get; set; }
       public int FailureCount { get; set; }
       public double ThroughputPerSecond { get; set; }
       public string Status { get; set; }
       public TimeSpan? ProcessingTime { get; set; }
   }
   ```

2. **P1-T2.2**: Add factory methods to SignalREventFactory
   ```csharp
   public ComprehensiveEntityProgressEvent CreateComprehensiveEntityProgress(
       string migrationId, 
       ComprehensiveEntityProgressOptions options)
   
   public OverallPipelineProgressEvent CreateOverallPipelineProgress(
       string migrationId, 
       OverallPipelineProgressOptions options)
   
   public PipelineThroughputEvent CreatePipelineThroughput(
       string migrationId, 
       PipelineThroughputOptions options)
   ```

3. **P1-T2.3**: Implement auto-calculation logic
   ```csharp
   // Auto-calculate progress percentage
   ProgressPercentage = options.TotalCount > 0 ? (double)options.ProcessedCount / options.TotalCount * 100 : 0;
   
   // Auto-calculate success rate
   SuccessRate = options.ProcessedCount > 0 ? (double)options.SuccessCount / options.ProcessedCount * 100 : 0;
   ```

**Acceptance Criteria:**
- ✅ Factory methods create events with auto-populated base properties
- ✅ Progress percentages are calculated automatically
- ✅ All validation logic works correctly
- ✅ Maintains backward compatibility with existing events

---

#### **P1-T3: Create PipelineProgressAggregator** ⭐  
**Priority**: Critical | **Effort**: 4 hours | **Status**: 🔴 Pending

**Overview**: Implement thread-safe multi-channel progress aggregation service that coordinates progress from all entity processing channels and broadcasts comprehensive updates.

**Files to Create:**
```
src/BigCommerce.Migration.Infrastructure/Services/PipelineProgressAggregator.cs
```

**Sub-tasks:**
1. **P1-T3.1**: Implement core aggregation infrastructure
   ```csharp
   public class PipelineProgressAggregator : IPipelineProgressAggregator
   {
       private readonly ConcurrentDictionary<string, EntityChannelProgress> _entityProgress;
       private readonly ISignalREventFactory _signalREventFactory;
       private readonly IProgressReporter _progressReporter;
       private readonly Timer _broadcastTimer;
       
       public async Task UpdateEntityProgressAsync(
           string migrationId,
           string entityType, 
           EntityChannelProgress progress)
       
       public async Task BroadcastAggregatedProgressAsync(string migrationId)
   }
   ```

2. **P1-T3.2**: Implement thread-safe progress tracking
   ```csharp
   public class EntityChannelProgress
   {
       public string EntityType { get; set; }
       public int TotalCount { get; set; }
       public int ProcessedCount { get; set; }
       public int SuccessCount { get; set; }
       public int FailureCount { get; set; }
       public DateTime StartTime { get; set; }
       public DateTime LastUpdateTime { get; set; }
       public double CurrentThroughput => CalculateThroughput();
   }
   ```

3. **P1-T3.3**: Implement real-time throughput calculation
   ```csharp
   private double CalculateThroughput()
   {
       var elapsed = DateTime.UtcNow - LastUpdateTime;
       if (elapsed.TotalSeconds < 1) return PreviousThroughput;
       
       var recentProgress = ProcessedCount - LastProcessedCount;
       return recentProgress / elapsed.TotalSeconds;
   }
   ```

4. **P1-T3.4**: Implement ETA calculation
   ```csharp
   private TimeSpan? CalculateETA()
   {
       var remainingEntities = TotalEntitiesAllTypes - ProcessedEntitiesAllTypes;
       if (remainingEntities <= 0 || OverallThroughput <= 0) return null;
       
       return TimeSpan.FromSeconds(remainingEntities / OverallThroughput);
   }
   ```

5. **P1-T3.5**: Implement rate-limited broadcasting
   ```csharp
   private readonly SemaphoreSlim _broadcastSemaphore = new(1);
   private DateTime _lastBroadcast = DateTime.MinValue;
   private const int MinBroadcastIntervalMs = 1000; // Max 1 update/second
   ```

**Acceptance Criteria:**
- ✅ Thread-safe aggregation across multiple channels
- ✅ Accurate throughput calculation (entities/second)
- ✅ ETA calculation based on current throughput
- ✅ Rate-limited SignalR broadcasting (max 1/second)
- ✅ Memory efficient - cleanup completed channels

---

### **PHASE 2: FRONTEND COMPONENTS (12 hours)**

#### **P2-T1: Create Main Page Component** ⭐  
**Priority**: High | **Effort**: 3 hours | **Status**: 🔴 Pending

**Overview**: Create the main comprehensive entity migration page component that matches the target design from the screenshot.

**Files to Create:**
```
src/BigCommerce.Migration.Dashboard/src/components/Views/ComprehensiveEntityMigrationPage.tsx
```

**Sub-tasks:**
1. **P2-T1.1**: Create main page layout
   ```typescript
   export const ComprehensiveEntityMigrationPage: React.FC = () => {
     const { migrationId } = useParams<{ migrationId: string }>();
     const { progress, isConnected } = useComprehensiveEntityProgress(migrationId);
     
     return (
       <Box sx={{ p: 3 }}>
         <ComprehensiveProgressHeader progress={progress} />
         <OverallProgressSection progress={progress} />
         <EntityBreakdownGrid progress={progress} />
         <MigrationStatusFooter progress={progress} />
       </Box>
     );
   };
   ```

2. **P2-T1.2**: Implement responsive layout
   ```typescript
   const useResponsiveLayout = () => {
     const theme = useTheme();
     const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
     const isTablet = useMediaQuery(theme.breakpoints.down('md'));
     
     return { isMobile, isTablet, gridColumns: isMobile ? 1 : isTablet ? 2 : 4 };
   };
   ```

3. **P2-T1.3**: Add navigation and routing integration
   ```typescript
   // In App.tsx
   <Route 
     path="/migrations/:migrationId/comprehensive" 
     element={<ComprehensiveEntityMigrationPage />} 
   />
   ```

**Acceptance Criteria:**
- ✅ Page layout matches target design
- ✅ Responsive design works on mobile/tablet/desktop
- ✅ Navigation integration works correctly
- ✅ Loading and error states handled gracefully

---

#### **P2-T2: Create Progress Components** ⭐  
**Priority**: High | **Effort**: 4 hours | **Status**: 🔴 Pending

**Overview**: Create individual progress components that display real-time entity progress matching the screenshot design.

**Files to Create:**
```
src/BigCommerce.Migration.Dashboard/src/components/Progress/
├── ComprehensiveProgressHeader.tsx
├── OverallProgressSection.tsx  
├── EntityBreakdownGrid.tsx
├── EntityProgressCard.tsx
└── MigrationStatusFooter.tsx
```

**Sub-tasks:**
1. **P2-T2.1**: Create ComprehensiveProgressHeader
   ```typescript
   export const ComprehensiveProgressHeader: React.FC<{
     progress: OverallPipelineProgress;
   }> = ({ progress }) => (
     <Box sx={{ mb: 3 }}>
       <Typography variant="h4" gutterBottom>
         📊 Comprehensive Entity Migration Progress: {progress.overallProgressPercentage.toFixed(1)}% Complete
       </Typography>
     </Box>
   );
   ```

2. **P2-T2.2**: Create OverallProgressSection
   ```typescript
   export const OverallProgressSection: React.FC<{
     progress: OverallPipelineProgress;
   }> = ({ progress }) => (
     <Card sx={{ mb: 3 }}>
       <CardContent>
         <Typography variant="h6">Overall Progress:</Typography>
         <LinearProgress 
           variant="determinate" 
           value={progress.overallProgressPercentage}
           sx={{ height: 10, borderRadius: 5 }}
         />
         <Typography variant="body2" color="textSecondary">
           {progress.processedEntitiesAllTypes}/{progress.totalEntitiesAllTypes} entities
         </Typography>
       </CardContent>
     </Card>
   );
   ```

3. **P2-T2.3**: Create EntityProgressCard
   ```typescript
   export const EntityProgressCard: React.FC<{
     entityType: string;
     progress: EntityTypeProgress;
   }> = ({ entityType, progress }) => {
     const statusIcon = getStatusIcon(progress.status);
     const statusColor = getStatusColor(progress.status);
     
     return (
       <Card>
         <CardContent>
           <Box display="flex" alignItems="center" justifyContent="space-between">
             <Typography variant="h6" textTransform="capitalize">
               {statusIcon} {entityType}:
             </Typography>
             <Typography variant="body2" color={statusColor}>
               {progress.throughputPerSecond.toFixed(1)}/sec
             </Typography>
           </Box>
           <LinearProgress 
             variant="determinate" 
             value={progress.progressPercentage}
             sx={{ my: 1, height: 8, borderRadius: 4 }}
           />
           <Typography variant="body2">
             {progress.processedCount}/{progress.totalCount} ({progress.progressPercentage.toFixed(0)}%)
           </Typography>
         </CardContent>
       </Card>
     );
   };
   ```

4. **P2-T2.4**: Create EntityBreakdownGrid
   ```typescript
   export const EntityBreakdownGrid: React.FC<{
     progress: OverallPipelineProgress;
   }> = ({ progress }) => (
     <Box sx={{ mb: 3 }}>
       <Typography variant="h6" gutterBottom>Entity Breakdown:</Typography>
       <Grid container spacing={2}>
         {Object.entries(progress.entityBreakdown).map(([entityType, entityProgress]) => (
           <Grid item xs={12} sm={6} md={3} key={entityType}>
             <EntityProgressCard 
               entityType={entityType} 
               progress={entityProgress} 
             />
           </Grid>
         ))}
       </Grid>
     </Box>
   );
   ```

5. **P2-T2.5**: Create MigrationStatusFooter
   ```typescript
   export const MigrationStatusFooter: React.FC<{
     progress: OverallPipelineProgress;
   }> = ({ progress }) => (
     <Card>
       <CardContent>
         <Typography variant="body1">
           Status: Processing • Elapsed: {formatDuration(progress.elapsedTime)} • 
           ETA: {progress.estimatedTimeRemaining ? formatDuration(progress.estimatedTimeRemaining) : 'Calculating...'}
         </Typography>
       </CardContent>
     </Card>
   );
   ```

**Acceptance Criteria:**
- ✅ All components render correctly with mock data
- ✅ Progress bars display correct percentages
- ✅ Throughput displays in entities/second format
- ✅ Status icons (✅ ❌ 🔄) show correctly
- ✅ Time formatting displays correctly (2m 34s format)

---

#### **P2-T3: Create SignalR Hook** ⭐  
**Priority**: High | **Effort**: 3 hours | **Status**: 🔴 Pending

**Overview**: Create React hook for comprehensive entity progress that handles SignalR connection and real-time data updates.

**Files to Create:**
```
src/BigCommerce.Migration.Dashboard/src/hooks/useComprehensiveEntityProgress.ts
```

**Sub-tasks:**
1. **P2-T3.1**: Implement base hook structure
   ```typescript
   export const useComprehensiveEntityProgress = (migrationId: string) => {
     const [progress, setProgress] = useState<OverallPipelineProgress | null>(null);
     const [entityProgress, setEntityProgress] = useState<Record<string, EntityTypeProgress>>({});
     const [throughputMetrics, setThroughputMetrics] = useState<PipelineThroughputMetrics | null>(null);
     const [isConnected, setIsConnected] = useState(false);
     const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
     
     // SignalR connection management
     const connectionRef = useRef<HubConnection | null>(null);
   };
   ```

2. **P2-T3.2**: Implement SignalR event handlers
   ```typescript
   const setupEventHandlers = useCallback((connection: HubConnection) => {
     connection.on('ComprehensiveEntityProgressUpdated', (data: ComprehensiveEntityProgressEvent) => {
       setEntityProgress(prev => ({
         ...prev,
         [data.entityType]: {
           totalCount: data.totalCount,
           processedCount: data.processedCount,
           successCount: data.successCount,
           failureCount: data.failureCount,
           progressPercentage: data.progressPercentage,
           throughputPerSecond: data.throughputPerSecond,
           status: data.status
         }
       }));
       setLastUpdated(new Date());
     });
     
     connection.on('OverallPipelineProgressUpdated', (data: OverallPipelineProgressEvent) => {
       setProgress(data);
       setLastUpdated(new Date());
     });
     
     connection.on('PipelineThroughputUpdated', (data: PipelineThroughputEvent) => {
       setThroughputMetrics(data);
       setLastUpdated(new Date());
     });
   }, []);
   ```

3. **P2-T3.3**: Implement connection management
   ```typescript
   const connect = useCallback(async () => {
     try {
       const connection = new HubConnectionBuilder()
         .withUrl(`${apiBaseUrl}/comprehensiveProgressHub`)
         .withAutomaticReconnect()
         .build();
       
       setupEventHandlers(connection);
       await connection.start();
       await connection.invoke('JoinMigrationGroup', migrationId);
       
       connectionRef.current = connection;
       setIsConnected(true);
     } catch (error) {
       console.error('SignalR connection failed:', error);
       setIsConnected(false);
     }
   }, [migrationId, setupEventHandlers]);
   ```

4. **P2-T3.4**: Implement cleanup and reconnection
   ```typescript
   useEffect(() => {
     if (migrationId) {
       connect();
     }
     
     return () => {
       if (connectionRef.current) {
         connectionRef.current.stop();
         connectionRef.current = null;
         setIsConnected(false);
       }
     };
   }, [migrationId, connect]);
   ```

**Acceptance Criteria:**
- ✅ SignalR connection establishes successfully
- ✅ Real-time events update state correctly
- ✅ Connection recovery works after network interruption
- ✅ Memory cleanup prevents leaks
- ✅ Error handling provides meaningful feedback

---

#### **P2-T4: Update Type Definitions** ⭐  
**Priority**: Medium | **Effort**: 2 hours | **Status**: 🔴 Pending

**Overview**: Update TypeScript type definitions to support comprehensive entity progress data structures.

**Files to Modify:**
```
src/BigCommerce.Migration.Dashboard/src/types/index.ts
```

**Sub-tasks:**
1. **P2-T4.1**: Add comprehensive entity progress types
   ```typescript
   export interface EntityTypeProgress {
     totalCount: number;
     processedCount: number;
     successCount: number;
     failureCount: number;
     progressPercentage: number;
     throughputPerSecond: number;
     status: 'pending' | 'processing' | 'completed' | 'failed';
     processingTime?: string;
     startTime?: string;
   }
   
   export interface OverallPipelineProgress {
     overallProgressPercentage: number;
     totalEntitiesAllTypes: number;
     processedEntitiesAllTypes: number;
     entityBreakdown: Record<string, EntityTypeProgress>;
     elapsedTime: string;
     estimatedTimeRemaining?: string;
     currentPhase: string;
     overallThroughput: number;
   }
   ```

2. **P2-T4.2**: Add pipeline throughput types
   ```typescript
   export interface PipelineThroughputMetrics {
     entityTypeThroughput: Record<string, number>;
     overallThroughput: number;
     activeChannels: number;
     channelQueueDepth: Record<string, number>;
     timestamp: string;
   }
   ```

3. **P2-T4.3**: Add SignalR event types
   ```typescript
   export interface ComprehensiveEntityProgressEvent {
     migrationId: string;
     entityType: string;
     totalCount: number;
     processedCount: number;
     successCount: number;
     failureCount: number;
     progressPercentage: number;
     throughputPerSecond: number;
     status: string;
     timestamp: string;
   }
   ```

**Acceptance Criteria:**
- ✅ All new types compile without errors
- ✅ Types match backend event structures
- ✅ Existing types remain compatible
- ✅ IntelliSense provides proper type hints

---

### **PHASE 3: INTEGRATION & TESTING (4 hours)**

#### **P3-T1: Integration Testing** ⭐  
**Priority**: Medium | **Effort**: 2 hours | **Status**: 🔴 Pending

**Overview**: Create comprehensive integration tests for the new dashboard functionality.

**Files to Create:**
```
src/BigCommerce.Migration.Dashboard/src/components/Tests/ComprehensiveEntityProgressTest.tsx
```

**Sub-tasks:**
1. **P3-T1.1**: Create mock data generators
   ```typescript
   export const createMockOverallProgress = (): OverallPipelineProgress => ({
     overallProgressPercentage: 71.3,
     totalEntitiesAllTypes: 1200,
     processedEntitiesAllTypes: 856,
     entityBreakdown: {
       options: { totalCount: 200, processedCount: 200, progressPercentage: 100, throughputPerSecond: 15.2, status: 'completed' },
       modifiers: { totalCount: 180, processedCount: 150, progressPercentage: 83.3, throughputPerSecond: 8.7, status: 'processing' },
       images: { totalCount: 520, processedCount: 320, progressPercentage: 61.5, throughputPerSecond: 12.1, status: 'processing' },
       reviews: { totalCount: 200, processedCount: 186, progressPercentage: 93.0, throughputPerSecond: 9.3, status: 'processing' }
     },
     elapsedTime: '2m 34s',
     estimatedTimeRemaining: '1m 12s',
     currentPhase: 'Phase 2: Comprehensive Entity Migration',
     overallThroughput: 11.3
   });
   ```

2. **P3-T1.2**: Test component rendering
   ```typescript
   test('renders comprehensive entity migration page correctly', () => {
     const mockProgress = createMockOverallProgress();
     render(<ComprehensiveEntityMigrationPage />, {
       initialState: { progress: mockProgress }
     });
     
     expect(screen.getByText(/71.3% Complete/)).toBeInTheDocument();
     expect(screen.getByText(/856\/1,200 entities/)).toBeInTheDocument();
   });
   ```

3. **P3-T1.3**: Test SignalR event handling
   ```typescript
   test('handles SignalR events correctly', async () => {
     const { rerender } = renderHook(() => useComprehensiveEntityProgress('test-migration'));
     
     // Simulate SignalR event
     act(() => {
       mockSignalRConnection.invoke('ComprehensiveEntityProgressUpdated', {
         entityType: 'options',
         processedCount: 150,
         totalCount: 200,
         throughputPerSecond: 15.2
       });
     });
     
     // Verify state update
     expect(result.current.entityProgress.options.processedCount).toBe(150);
   });
   ```

**Acceptance Criteria:**
- ✅ All components render correctly with mock data
- ✅ SignalR event simulation works correctly
- ✅ State updates trigger proper re-renders
- ✅ Error scenarios are handled gracefully

---

#### **P3-T2: Navigation Integration** ⭐  
**Priority**: Medium | **Effort**: 2 hours | **Status**: 🔴 Pending

**Overview**: Integrate the new comprehensive entity migration page into the existing dashboard navigation.

**Files to Modify:**
```
src/BigCommerce.Migration.Dashboard/src/App.tsx
src/BigCommerce.Migration.Dashboard/src/components/Layout/DashboardSidebar.tsx
```

**Sub-tasks:**
1. **P3-T2.1**: Add route configuration
   ```typescript
   // In App.tsx
   <Routes>
     <Route path="/" element={<MigrationOverview />} />
     <Route path="/migrations/:migrationId" element={<EnhancedMigrationDashboard />} />
     <Route path="/migrations/:migrationId/comprehensive" element={<ComprehensiveEntityMigrationPage />} />
     <Route path="/history" element={<HistoryView />} />
   </Routes>
   ```

2. **P3-T2.2**: Add navigation menu item
   ```typescript
   // In DashboardSidebar.tsx
   const menuItems = [
     { label: 'Migration Overview', path: '/', icon: <DashboardIcon /> },
     { label: 'Migration Details', path: `/migrations/${migrationId}`, icon: <DetailIcon /> },
     { label: 'Comprehensive Progress', path: `/migrations/${migrationId}/comprehensive`, icon: <TimelineIcon /> },
     { label: 'History', path: '/history', icon: <HistoryIcon /> }
   ];
   ```

3. **P3-T2.3**: Add breadcrumb navigation
   ```typescript
   const getBreadcrumbs = (pathname: string) => {
     if (pathname.includes('/comprehensive')) {
       return [
         { label: 'Migration', path: `/migrations/${migrationId}` },
         { label: 'Comprehensive Progress', path: pathname }
       ];
     }
     // ... other breadcrumb logic
   };
   ```

4. **P3-T2.4**: Add deep linking support
   ```typescript
   // Allow direct navigation to comprehensive page
   useEffect(() => {
     if (migrationPhase === 'comprehensive-entities') {
       navigate(`/migrations/${migrationId}/comprehensive`);
     }
   }, [migrationPhase, migrationId]);
   ```

**Acceptance Criteria:**
- ✅ Navigation menu shows comprehensive progress option
- ✅ Direct URL navigation works correctly
- ✅ Breadcrumb navigation shows proper hierarchy
- ✅ Back navigation returns to correct page

---

## 🔗 **DEPENDENCIES & PREREQUISITES**

### **Must Have (Blocking)**
1. **Pipeline Parallelism Core Implementation**: PipelineParallelProcessor must be implemented first
2. **SignalR Infrastructure**: Current SignalREventFactory and broadcasting system must be stable
3. **Basic Dashboard**: Existing dashboard components must be working correctly

### **Nice to Have (Non-blocking)**
1. **Enhanced Error Handling**: Circuit breaker pattern implementation
2. **Performance Monitoring**: Additional metrics collection
3. **Advanced Filtering**: User-selectable entity types for display

---

## ⚡ **PERFORMANCE TARGETS**

### **Real-time Updates**
- **SignalR Event Latency**: < 500ms from backend to UI update
- **UI Render Performance**: < 100ms for progress bar updates
- **Memory Usage**: < 50MB for 24-hour continuous operation

### **User Experience**
- **Initial Load**: < 2 seconds to display initial progress
- **Update Frequency**: 1-4 updates per second (rate-limited)
- **Responsiveness**: UI remains interactive during rapid updates

---

## 🎨 **DESIGN SPECIFICATIONS**

### **Color Scheme**
```typescript
const EntityStatusColors = {
  completed: '#4caf50',   // Green
  processing: '#2196f3',  // Blue  
  failed: '#f44336',      // Red
  pending: '#ff9800'      // Orange
};
```

### **Typography Scale**
- **Page Title**: h4 (34px) - "Comprehensive Entity Migration Progress"
- **Section Headers**: h6 (20px) - "Entity Breakdown:", "Overall Progress:"
- **Entity Names**: subtitle1 (16px) - "Options:", "Modifiers:"
- **Metrics**: body2 (14px) - "15.2/sec", "200/200 (100%)"

### **Spacing & Layout**
- **Page Padding**: 24px all sides
- **Component Spacing**: 24px between major sections
- **Card Padding**: 16px internal padding
- **Grid Spacing**: 16px between entity cards

---

## 📋 **ACCEPTANCE CRITERIA SUMMARY**

### **Functional Requirements**
- ✅ Displays real-time progress for all 4 entity types simultaneously
- ✅ Shows individual throughput (entities/second) for each entity type
- ✅ Calculates and displays overall progress percentage accurately
- ✅ Provides time estimates (elapsed + ETA) based on current throughput
- ✅ Updates in real-time via SignalR without page refresh

### **Non-Functional Requirements**
- ✅ Responsive design works on mobile, tablet, and desktop
- ✅ Performance remains smooth during rapid updates (>50/minute)
- ✅ Memory usage stays stable during long-running migrations
- ✅ Graceful degradation when SignalR connection is lost

### **User Experience Requirements**
- ✅ Visual design matches provided screenshot closely
- ✅ Clear status indicators (✅ ❌ 🔄) for each entity type
- ✅ Intuitive progress representation with percentages and counts
- ✅ Consistent navigation and breadcrumb integration

---

## 🚨 **RISK MITIGATION**

### **Technical Risks**
1. **SignalR Event Overload**: Implement rate limiting (max 4 events/second)
2. **UI Performance Issues**: Use React.memo and debouncing for updates
3. **Memory Leaks**: Proper cleanup of SignalR connections and timers
4. **Browser Compatibility**: Test on Chrome, Firefox, Safari, Edge

### **Project Risks**
1. **Timeline Delays**: Prioritize core functionality over advanced features
2. **Integration Complexity**: Start with simple mock data before full integration
3. **Design Changes**: Build components modular for easy adjustment
4. **Testing Scope**: Focus on happy path scenarios first

---

## 📖 **RELATED DOCUMENTATION**

- **Main Implementation Plan**: `docs/Pipeline-Parallelism-Dashboard-Implementation-Plan.md`
- **Progress Aggregation Design**: `docs/Pipeline-Parallelism-Progress-Aggregation-Design.md`
- **Enhanced Product Migration**: `docs/Enhanced-Product-Migration-FINAL-SUMMARY.md`
- **SignalR Configuration**: `src/BigCommerce.Migration.Functions/Configuration/SignalR-Configuration-Guide.md`