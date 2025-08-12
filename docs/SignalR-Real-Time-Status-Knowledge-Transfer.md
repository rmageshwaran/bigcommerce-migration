# SignalR Real-Time Status System - Knowledge Transfer Document

## 📋 Document Purpose

This document provides comprehensive technical insights into the BigCommerce Migration project's SignalR real-time status update system. It covers the complete architecture from event creation to frontend consumption, enabling developers to understand, maintain, and extend the real-time capabilities.

---

## 🎯 Executive Summary

The BigCommerce Migration project implements a **centralized, queue-based SignalR system** that:

- **Provides real-time migration status updates** to dashboard users
- **Coordinates 35+ event creation points** through a centralized factory pattern
- **Uses Azure Storage Queues** for reliable message delivery
- **Supports live cancellation** with soft cancellation state propagation
- **Maintains 100% build success** with comprehensive test coverage
- **Automatically converts PascalCase to camelCase** for frontend compatibility

---

## 🏗️ System Architecture Overview

### High-Level Flow

```
Backend Components → SignalREventFactory → ProgressEventPublisher → Azure Storage Queue → Azure Functions → SignalR Hub → Frontend Dashboard
```

### Key Design Principles

1. **Centralized Event Creation**: Single `SignalREventFactory` for all event types
2. **Queue-Based Decoupling**: Asynchronous processing via Azure Storage Queues
3. **Automatic Property Conversion**: PascalCase (backend) to camelCase (frontend)
4. **Consistent Event Structure**: Standardized base properties across all events
5. **Graceful Degradation**: Non-blocking error handling for SignalR failures

---

## 🧠 Core Components Deep Dive

### 1. SignalREventFactory - Centralized Event Creation

**Location**: `src/BigCommerce.Migration.Core/Services/SignalREventFactory.cs`

**Purpose**: Single source of truth for all SignalR event creation across the system

**Key Features**:
- **Auto-populated base properties**: Timestamp, IsCancelled, HubMethod
- **Built-in validation**: Required property checking
- **Consistent naming**: Enforced property naming conventions
- **Type safety**: Strongly-typed event creation with options pattern

**Event Types Supported**:
```csharp
// Migration-level progress
var progressEvent = _signalREventFactory.CreateMigrationProgress(migrationId, new MigrationProgressOptions
{
    OverallProgress = 75.5,
    Status = "running",
    TotalEntities = 1000,
    ProcessedEntities = 755,
    FailedEntities = 12,
    CurrentEntityType = "products"
});

// Entity-level progress  
var entityEvent = _signalREventFactory.CreateEntityProgress(migrationId, new EntityProgressOptions
{
    EntityType = "categories",
    TotalCount = 50,
    ProcessedCount = 25,
    Status = "processing",
    ProcessingTime = TimeSpan.FromMinutes(2.5)
});

// Batch-level progress
var batchEvent = _signalREventFactory.CreateBatchProgress(migrationId, new BatchProgressOptions
{
    EntityType = "products",
    BatchNumber = 5,
    TotalBatches = 20,
    BatchSize = 100,
    Status = "completed"
});

// Error events
var errorEvent = _signalREventFactory.CreateErrorProgress(migrationId, new ErrorProgressOptions
{
    ErrorMessage = "API timeout during product creation",
    ErrorType = "timeout",
    EntityType = "products",
    Severity = "warning"
});

// Status changes
var statusEvent = _signalREventFactory.CreateStatusProgress(migrationId, new StatusProgressOptions
{
    Status = "completed",
    StatusMessage = "Migration completed successfully",
    CompletionTime = DateTimeOffset.UtcNow
});
```

**Auto-Calculated Properties**:
```csharp
// Factory automatically calculates these properties
SuccessfulEntities = ProcessedEntities - FailedEntities,
ElapsedTime = StartTime.HasValue ? UtcNow - StartTime.Value : null,
HubMethod = StandardizedHubMethods[eventType], // "MigrationProgressUpdated", etc.
Timestamp = _dateTimeProvider.UtcNow,
IsCancelled = options.IsCancelled ?? false
```

### 2. SignalRMessageConverter - Frontend Compatibility

**Location**: `src/BigCommerce.Migration.Core/Services/SignalRMessageConverter.cs`

**Purpose**: Handles consistent naming between backend (PascalCase) and frontend (camelCase)

**Key Features**:
- **Automatic case conversion**: PascalCase → camelCase
- **Property mapping**: Handles missing/null properties gracefully
- **JSON serialization**: Optimized for frontend consumption
- **Metadata injection**: Adds event metadata for frontend routing

**Conversion Example**:
```csharp
// Backend event (PascalCase)
var backendEvent = new MigrationProgressEvent
{
    MigrationId = "mig-123",
    OverallProgress = 75.5,
    ProcessedEntities = 755,
    TotalEntities = 1000,
    CurrentEntityType = "products"
};

// Frontend object (camelCase) - automatically converted
{
    "migrationId": "mig-123",
    "overallProgress": 75.5,
    "processedEntities": 755,
    "totalEntities": 1000,
    "currentEntityType": "products",
    "timestamp": "2024-01-15T10:30:00Z",
    "hubMethod": "MigrationProgressUpdated"
}
```

### 3. ProgressEventPublisher - Queue Integration

**Location**: `src/BigCommerce.Migration.Infrastructure/Services/ProgressEventPublisher.cs`

**Purpose**: Publishes progress events to Azure Storage Queue for SignalR broadcasting

**Queue Configuration**:
- **Queue Name**: `signalr-progress-events`
- **Encoding**: Base64 for JSON message compatibility
- **Reliability**: Auto-retry with exponential backoff
- **Validation**: Null checking and required property validation

**Publishing Process**:
```csharp
public async Task PublishAsync(ProgressEvent progressEvent, CancellationToken cancellationToken)
{
    // 1. Validate event
    if (progressEvent?.MigrationId == null) return;
    
    // 2. Ensure queue exists
    await _progressQueueService.EnsureQueueExistsAsync("signalr-progress-events", cancellationToken);
    
    // 3. Serialize to JSON with camelCase
    var messageContent = JsonSerializer.Serialize(progressEvent, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    });
    
    // 4. Send to queue (Base64 encoded)
    await _progressQueueService.SendJsonMessageAsync("signalr-progress-events", messageContent, cancellationToken);
}
```

### 4. SignalRProgressFunctions - Azure Functions Integration

**Location**: `src/BigCommerce.Migration.Functions/Functions/SignalRProgressFunctions.cs`

**Purpose**: Processes queue messages and broadcasts to SignalR clients

**Key Functions**:

#### ProcessProgressEvents
- **Trigger**: Queue trigger on `signalr-progress-events`
- **Purpose**: Converts queue messages to SignalR broadcasts
- **Features**: Soft cancellation filtering, detailed logging, error handling

```csharp
[Function("ProcessProgressEvents")]
[SignalROutput(HubName = "migrationhub", ConnectionStringSetting = "AzureSignalR")]
public SignalRMessageAction ProcessProgressEvents(
    [QueueTrigger("signalr-progress-events")] string queueMessage)
{
    // 1. Deserialize progress event from queue
    var progressEvent = JsonSerializer.Deserialize<ProgressEvent>(queueMessage);
    
    // 2. Filter cancelled migrations (soft cancellation)
    if (IsProgressTypeEvent(progressEvent) && progressEvent.IsCancelled)
    {
        return null; // Don't broadcast progress for cancelled migrations
    }
    
    // 3. Convert to frontend-compatible format
    var frontendMessage = _signalRMessageConverter.CreateSignalRMessage(progressEvent);
    
    // 4. Create SignalR broadcast action
    return new SignalRMessageAction(progressEvent.HubMethod, new[] { frontendMessage });
}
```

#### SignalRNegotiation
- **Endpoint**: `POST /api/SignalRNegotiation`
- **Purpose**: Provides connection info for SignalR clients
- **Features**: CORS support, connection string management

```csharp
[Function("SignalRNegotiation")]
public async Task<HttpResponseData> GetSignalRConnectionInfo(
    [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options")] HttpRequestData req,
    [SignalRConnectionInfoInput(HubName = "migrationhub")] SignalRConnectionInfo connectionInfo)
{
    // Handle CORS preflight
    if (req.Method.Equals("OPTIONS")) return CreateCorsResponse(req);
    
    // Return connection info for SignalR client
    var response = req.CreateResponse(HttpStatusCode.OK);
    response.Headers.Add("Access-Control-Allow-Origin", "*");
    await response.WriteAsJsonAsync(connectionInfo);
    return response;
}
```

---

## 🌐 Frontend Integration

### 1. SignalRService - Client Connection Management

**Location**: `src/BigCommerce.Migration.Dashboard/src/services/signalRService.ts`

**Purpose**: Manages SignalR connection and event handling on the frontend

**Key Features**:
- **Automatic reconnection**: 5 retry attempts with exponential backoff
- **Event listener management**: Type-safe event subscription/unsubscription
- **Connection state tracking**: Connected, disconnected, reconnecting states
- **Migration group joining**: Scoped event delivery

**Connection Setup**:
```typescript
export class SignalRService {
  private connection: signalR.HubConnection | null = null;
  
  async connect(): Promise<void> {
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/api/SignalRNegotiation', {
        transport: signalR.HttpTransportType.WebSockets,
        withCredentials: false
      })
      .withAutomaticReconnect([0, 2000, 10000, 30000, 60000]) // Progressive delay
      .configureLogging(signalR.LogLevel.Information)
      .build();
      
    await this.connection.start();
  }
  
  async joinMigrationGroup(migrationId: string): Promise<void> {
    await this.connection?.invoke('JoinMigrationGroup', migrationId);
  }
}
```

**Event Handling**:
```typescript
private setupEventHandlers(): void {
  // Migration progress updates
  this.connection.on('MigrationProgressUpdated', (eventData: any) => {
    const enrichedProgress = this.enrichMigrationProgressEvent(eventData);
    this.notifyListeners('migrationProgress', enrichedProgress);
  });

  // Entity progress updates  
  this.connection.on('EntityProgressUpdated', (eventData: any) => {
    // Status forwarding for completion detection
    if (eventData.status === 'completed') {
      this.notifyListeners('MigrationStatus', {
        migrationId: eventData.migrationId,
        status: 'completed',
        data: eventData
      });
    }
    
    this.notifyListeners('DetailedProgress', eventData);
    this.notifyListeners('entityUpdate', eventData);
  });

  // Batch progress updates
  this.connection.on('BatchProgressUpdated', (eventData: any) => {
    this.notifyListeners('batchUpdate', eventData);
  });

  // Error events
  this.connection.on('ErrorOccurred', (eventData: any) => {
    this.notifyListeners('errorUpdate', eventData);
  });

  // Status changes
  this.connection.on('MigrationStatusChanged', (eventData: any) => {
    const enrichedStatus = this.enrichMigrationStatusEvent(eventData);
    this.notifyListeners('MigrationStatus', enrichedStatus);
  });
}
```

### 2. React Hook Integration

**Location**: `src/BigCommerce.Migration.Dashboard/src/hooks/useRealTimeMigrationProgress.ts`

**Purpose**: Provides React components with real-time migration progress data

**Key Features**:
- **State management**: Centralized progress state with React hooks
- **Event aggregation**: Combines multiple event types into unified state
- **Error handling**: Graceful error tracking and recovery
- **Connection monitoring**: Real-time connection status updates

**Usage Example**:
```typescript
export const useRealTimeMigrationProgress = (options: RealTimeMigrationProgressOptions) => {
  const [state, setState] = useState<RealTimeMigrationProgressState>({
    migrationProgress: null,
    detailedProgress: {},
    events: [],
    errors: [],
    isConnected: false,
    connectionState: 'disconnected',
    lastHeartbeat: null
  });

  // Connection management
  useEffect(() => {
    const connect = async () => {
      await signalRService.connect();
      await signalRService.joinMigrationGroup(migrationId);
      
      // Set up event listeners
      const unsubscribeProgress = signalRService.on('MigrationProgressUpdated', handleProgressUpdate);
      const unsubscribeEntity = signalRService.on('EntityProgressUpdated', handleEntityUpdate);
      const unsubscribeBatch = signalRService.on('BatchProgressUpdated', handleBatchUpdate);
      // ... more listeners
    };
    
    connect();
    return () => cleanup(); // Unsubscribe on unmount
  }, [migrationId]);

  return {
    ...state,
    connect,
    disconnect,
    joinMigrationGroup: signalRService.joinMigrationGroup,
    leaveMigrationGroup: signalRService.leaveMigrationGroup
  };
};
```

### 3. Dashboard Context Integration

**Location**: `src/BigCommerce.Migration.Dashboard/src/context/DashboardContext.tsx`

**Purpose**: Provides application-wide state management for migration progress

**Key Features**:
- **Status normalization**: Handles backend/frontend status mismatches  
- **Progress aggregation**: Combines multiple progress sources
- **Real-time updates**: Automatically updates UI based on SignalR events
- **Connection management**: Tracks SignalR connection state

**State Management**:
```typescript
// Set up SignalR event listeners
useEffect(() => {
  const signalRService = getSignalRService();

  // Migration progress updates
  const progressUnsubscribe = signalRService.on('migrationProgress', (progress: MigrationProgress) => {
    // Status normalization for backend inconsistencies
    const normalizedProgress = { ...progress };
    
    if (progress.status === 'queued' || progress.status === 'pending') {
      const isActuallyRunning = 
        progress.processedEntities > 0 ||
        progress.currentPhase === 'Processing' ||
        progress.overallProgressPercentage > 0;
        
      if (isActuallyRunning) {
        normalizedProgress.status = 'in_progress';
      }
    }
    
    dispatch({ type: 'UPDATE_MIGRATION_PROGRESS', payload: normalizedProgress });
  });

  // Connection state management
  const connectionUnsubscribe = signalRService.on('connectionStateChanged', (connectionData) => {
    dispatch({
      type: 'SET_SIGNALR_CONNECTION',
      payload: {
        connectionId: connectionData.connectionId || '',
        isConnected: connectionData.state === 'Connected',
        lastConnected: connectionData.state === 'Connected' ? new Date() : undefined,
        connectionState: connectionData.state
      }
    });
  });

  return () => {
    progressUnsubscribe();
    connectionUnsubscribe();
  };
}, []);
```

---

## 📊 Event Types & Data Flow

### Migration Progress Events

**Hub Method**: `MigrationProgressUpdated`
**Trigger**: Overall migration progress changes
**Frequency**: Rate-limited to prevent spam (configurable interval)

**Data Structure**:
```typescript
interface MigrationProgressEvent {
  migrationId: string;
  overallProgress: number;        // 0-100 percentage
  status: string;                 // "queued" | "running" | "completed" | "failed" | "cancelled"
  totalEntities: number;
  processedEntities: number;
  failedEntities: number;
  successfulEntities: number;     // Auto-calculated
  currentEntityType: string;
  startTime?: DateTimeOffset;
  elapsedTime?: TimeSpan;
  estimatedCompletion?: DateTimeOffset;
  currentProcessing: Record<string, any>;
  timestamp: DateTimeOffset;
  isCancelled: boolean;
  cancellationReason?: string;
  cancelledAt?: DateTimeOffset;
}
```

### Entity Progress Events  

**Hub Method**: `EntityProgressUpdated`
**Trigger**: Individual entity type progress changes
**Use Case**: Detailed progress tracking per entity (categories, products, etc.)

**Data Structure**:
```typescript
interface EntityProgressEvent {
  migrationId: string;
  entityType: string;             // "categories" | "products" | "brands" | etc.
  totalCount: number;
  processedCount: number;
  successCount: number;           // Auto-calculated
  failureCount: number;           // Auto-calculated
  status: string;                 // "starting" | "processing" | "completed" | "failed"
  processingTime?: TimeSpan;
  currentBatch?: number;
  totalBatches?: number;
  timestamp: DateTimeOffset;
  isCancelled: boolean;
}
```

### Batch Progress Events

**Hub Method**: `BatchProgressUpdated`  
**Trigger**: Individual batch completion within entity processing
**Use Case**: Granular progress tracking for large entity sets

**Data Structure**:
```typescript
interface BatchProgressEvent {
  migrationId: string;
  entityType: string;
  batchNumber: number;
  totalBatches: number;
  batchSize: number;
  processedInBatch: number;
  successfulInBatch: number;
  failedInBatch: number;
  status: string;                 // "processing" | "completed" | "failed"
  processingTime?: TimeSpan;
  timestamp: DateTimeOffset;
}
```

### Error Events

**Hub Method**: `ErrorOccurred`
**Trigger**: Errors during migration processing
**Use Case**: Real-time error monitoring and debugging

**Data Structure**:
```typescript
interface ErrorProgressEvent {
  migrationId: string;
  errorMessage: string;
  errorType: string;              // "timeout" | "validation" | "api_error" | etc.
  entityType?: string;
  entityId?: string;
  severity: string;               // "info" | "warning" | "error" | "critical"
  errorDetails?: Record<string, any>;
  timestamp: DateTimeOffset;
  stackTrace?: string;
}
```

### Status Change Events

**Hub Method**: `MigrationStatusChanged`
**Trigger**: Migration status transitions
**Use Case**: Critical status changes (start, complete, fail, cancel)

**Data Structure**:
```typescript
interface StatusProgressEvent {
  migrationId: string;
  status: string;                 // "queued" | "running" | "completed" | "failed" | "cancelled"
  previousStatus?: string;
  statusMessage?: string;
  completionTime?: DateTimeOffset;
  finalStatistics?: {
    totalEntities: number;
    successfulEntities: number;
    failedEntities: number;
    totalTime: TimeSpan;
  };
  timestamp: DateTimeOffset;
}
```

---

## 🛡️ Live Cancellation Integration

### Soft Cancellation Pattern

The system implements **soft cancellation** where cancelled migrations continue to process but stop sending progress updates to avoid UI confusion.

**Backend Implementation**:
```csharp
// Filter cancelled migrations in Azure Functions
private static bool IsProgressTypeEvent(ProgressEvent progressEvent)
{
    return progressEvent.EventType switch
    {
        "progress" => true,  // Filter migration progress for cancelled
        "batch" => true,     // Filter batch progress for cancelled  
        "entity" => true,    // Filter entity progress for cancelled
        "error" => false,    // Always broadcast errors for debugging
        "status" => false,   // Always broadcast status changes (includes cancellation)
        _ => true
    };
}

// In ProcessProgressEvents function
if (IsProgressTypeEvent(progressEvent) && progressEvent.IsCancelled)
{
    _logger.LogDebug("🚫 [SOFT-CANCEL] Filtering progress event for cancelled migration {MigrationId}", 
        progressEvent.MigrationId);
    return null; // Don't broadcast to SignalR
}
```

**Cancellation State Propagation**:
```csharp
// All events include cancellation state
var progressEvent = _signalREventFactory.CreateMigrationProgress(migrationId, new MigrationProgressOptions
{
    // ... other properties
    IsCancelled = cancellationState.IsCancelled,
    CancellationReason = cancellationState.CancellationReason,
    CancelledAt = cancellationState.CancelledAt
});
```

**Frontend Handling**:
```typescript
// DashboardContext handles status changes including cancellation
const statusUnsubscribe = signalRService.on('MigrationStatus', (statusData: any) => {
  if (statusData.status === 'cancelled') {
    dispatch({ type: 'SET_MIGRATION_CANCELLED', payload: statusData });
    // Stop expecting further progress updates
  }
});
```

---

## ⚙️ Configuration & Setup

### Backend Configuration

**Azure Functions Configuration** (`appsettings.json`):
```json
{
  "ConnectionStrings": {
    "AzureSignalR": "Endpoint=https://your-signalr-service.service.signalr.net;AccessKey=your-access-key;Version=1.0;",
    "AzureWebJobsStorage": "DefaultEndpointsProtocol=https;AccountName=yourstorageaccount;AccountKey=your-key;EndpointSuffix=core.windows.net"
  },
  "Values": {
    "SignalRProgressQueueName": "signalr-progress-events"
  }
}
```

**Service Registration** (`ServiceCollectionExtensions.cs`):
```csharp
// SignalR services registration
services.AddScoped<ISignalREventFactory, SignalREventFactory>();
services.AddScoped<ISignalRMessageConverter, SignalRMessageConverter>();
services.AddScoped<IProgressEventPublisher, ProgressEventPublisher>();
services.AddScoped<IProgressQueueService, AzureProgressQueueService>();

// HTTP client for SignalR optimization
services.AddHttpClient("SignalR", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "BigCommerce-Migration/1.0");
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(15),
    MaxConnectionsPerServer = 10,
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5)
});
```

### Frontend Configuration

**Environment Configuration** (`environment.ts`):
```typescript
export const config = {
  signalR: {
    hubUrl: '/SignalRNegotiation',     // Negotiate endpoint
    hubName: 'migrationhub',           // Must match backend hub name
    reconnectAttempts: 5,              // Auto-reconnect attempts
    connectionTimeout: 30000,          // 30 second timeout
  },
  
  features: {
    enableRealtime: true,              // Feature flag for SignalR
    enableDebugLogging: false,         // Debug SignalR events
  }
};
```

**Vite Proxy Configuration** (`vite.config.ts`):
```typescript
export default defineConfig({
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:7071',  // Azure Functions local port
        changeOrigin: true,
        secure: false,
      },
      '/SignalRNegotiation': {
        target: 'http://localhost:7071',
        changeOrigin: true,
        secure: false,
      }
    }
  }
});
```

---

## 📈 Performance & Monitoring

### Rate Limiting & Optimization

**Backend Rate Limiting**:
```csharp
// SignalR update rate limiting in ProgressAggregator
private readonly int _signalRUpdateIntervalMs = 1000; // 1 second minimum interval
private DateTime _lastSignalRUpdate = DateTime.MinValue;

private async Task SendRateLimitedSignalRUpdateAsync(CancellationToken cancellationToken)
{
    lock (_signalRRateLimitLock)
    {
        var timeSinceLastUpdate = _dateTimeProvider.UtcNow - _lastSignalRUpdate;
        if (timeSinceLastUpdate.TotalMilliseconds < _signalRUpdateIntervalMs)
        {
            return; // Rate limited
        }
        _lastSignalRUpdate = _dateTimeProvider.UtcNow;
    }
    
    await SendSignalRUpdateAsync(force: false, cancellationToken);
}
```

**Queue Optimization**:
- **Base64 encoding**: Required for Azure Storage Queue JSON compatibility
- **Message batching**: Automatic batching within rate limit windows
- **Exponential backoff**: Retry logic for queue failures
- **Dead letter handling**: Failed message recovery

### Connection Management

**Frontend Connection Resilience**:
```typescript
// Automatic reconnection with progressive delays
.withAutomaticReconnect([0, 2000, 10000, 30000, 60000])

// Connection state monitoring
connection.onreconnecting((error) => {
  console.log('🔄 SignalR reconnecting:', error);
  this.notifyListeners('connectionStateChanged', { state: 'Reconnecting', error });
});

connection.onreconnected((connectionId) => {
  console.log('✅ SignalR reconnected:', connectionId);
  this.notifyListeners('connectionStateChanged', { state: 'Connected', connectionId });
  
  // Rejoin migration groups after reconnection
  this.rejoinGroups();
});
```

### Key Metrics to Monitor

| Metric Category | Key Indicators | Alert Thresholds |
|----------------|----------------|------------------|
| **Connection Health** | Active connections, Reconnection rate | >50% disconnected |
| **Message Throughput** | Messages/sec, Queue depth | Queue depth >1000 |
| **Latency** | Event-to-frontend delay, Queue processing time | >2 second delay |
| **Error Rates** | Connection failures, Message failures | >5% error rate |
| **Resource Usage** | Queue storage, SignalR unit consumption | >80% capacity |

### Logging & Debugging

**Backend Structured Logging**:
```csharp
_logger.LogInformation("📊 [SIGNALR-UPDATE] Sent progress: {ProcessedEntities}/{TotalEntities} ({Progress:F1}%), Status: {Status}",
    processedEntities, totalEntities, currentProgress, migrationStatus);

_logger.LogDebug("🔨 [SIGNALR-CREATE] SignalR payload:\n{PayloadJson}", payloadJson);

_logger.LogWarning("⚠️ [SOFT-CANCEL] Filtering progress event for cancelled migration {MigrationId}", migrationId);
```

**Frontend Debug Logging**:
```typescript
if (config.features.enableDebugLogging) {
  console.log('🎯 DEBUG: Received MigrationProgressUpdated:', eventData);
  console.log('🎯 DEBUG: Enriched MigrationProgress:', enrichedProgress);
}
```

---

## 🚨 Troubleshooting Guide

### Common Issues & Solutions

#### SignalR Connection Failures

**Symptoms**: Dashboard shows "Disconnected", no real-time updates
**Causes**:
- Incorrect SignalR connection string
- CORS configuration issues  
- Network connectivity problems
- Azure SignalR service outage

**Resolution Steps**:
1. **Verify Connection String**: Check `AzureSignalR` connection string in Azure Functions configuration
2. **Test Negotiate Endpoint**: Visit `/api/SignalRNegotiation` directly to verify function is working
3. **Check CORS Headers**: Ensure proper CORS configuration in Azure Functions
4. **Monitor Azure SignalR**: Check Azure SignalR service health in Azure Portal
5. **Network Diagnostics**: Test WebSocket connectivity from client environment

**Debug Commands**:
```bash
# Test negotiate endpoint
curl -X POST https://your-functions-app.azurewebsites.net/api/SignalRNegotiation

# Check Azure SignalR service status
az signalr list --resource-group your-rg --query "[].{Name:name,State:provisioningState}"
```

#### Missing Real-Time Updates

**Symptoms**: Connection established but no progress updates received
**Causes**:
- Queue processing failures
- Event filtering (soft cancellation)
- Rate limiting blocking updates
- Migration not generating events

**Resolution Steps**:
1. **Check Queue Health**: Verify `signalr-progress-events` queue has messages
2. **Review Queue Processing**: Check Azure Functions logs for `ProcessProgressEvents` function
3. **Validate Migration Status**: Ensure migration is actually running and generating events
4. **Check Rate Limiting**: Verify rate limiting isn't blocking too many updates
5. **Test Event Creation**: Manually trigger test events to verify pipeline

**Debug Queries**:
```bash
# Check queue message count
az storage queue metadata show --name signalr-progress-events --account-name yourstorageaccount

# View recent function logs
az functionapp logs tail --name your-functions-app --resource-group your-rg
```

#### Frontend Event Processing Issues

**Symptoms**: Events received but UI not updating correctly
**Causes**:
- Event data format mismatches
- React state update issues
- Event listener memory leaks
- Component unmounting during updates

**Resolution Steps**:
1. **Browser Console**: Check for JavaScript errors during event processing
2. **Event Data Validation**: Verify event data structure matches expected format
3. **State Updates**: Ensure React state updates are properly batched
4. **Memory Leaks**: Check for proper event listener cleanup in useEffect
5. **Component Lifecycle**: Verify components are still mounted when updates arrive

**Debug Code**:
```typescript
// Add debug logging to event handlers
const handleProgressUpdate = useCallback((progress: MigrationProgress) => {
  console.log('🎯 Progress Update:', {
    migrationId: progress.migrationId,
    status: progress.status,
    percentage: progress.overallProgressPercentage,
    processedEntities: progress.processedEntities
  });
  
  // Validate expected properties
  if (!progress.migrationId) {
    console.error('❌ Missing migrationId in progress update');
  }
  
  setState(prev => ({ ...prev, migrationProgress: progress }));
}, []);
```

#### Queue Processing Delays

**Symptoms**: Significant delays between backend events and frontend updates
**Causes**:
- Azure Storage Queue throttling
- Function app cold starts
- High message volume overwhelming processing
- Network latency between services

**Resolution Steps**:
1. **Scale Function App**: Increase Azure Functions plan or switch to dedicated
2. **Optimize Queue Settings**: Adjust visibility timeout and batch size
3. **Monitor Function Performance**: Check execution time and memory usage
4. **Review Message Format**: Ensure messages are optimally sized
5. **Consider Premium Features**: Use Azure SignalR Premium for better performance

### Error Recovery Patterns

**Connection Recovery**:
```typescript
// Implement progressive backoff for connection failures
const connectWithRetry = async (retryCount: number = 0): Promise<void> => {
  try {
    await signalRService.connect();
    await signalRService.joinMigrationGroup(migrationId);
  } catch (error) {
    if (retryCount < maxRetries) {
      const delay = Math.min(1000 * Math.pow(2, retryCount), 30000);
      setTimeout(() => connectWithRetry(retryCount + 1), delay);
    } else {
      console.error('❌ Failed to establish SignalR connection after max retries');
      // Fall back to polling mode
      enablePollingFallback();
    }
  }
};
```

**Queue Processing Recovery**:
```csharp
// Implement dead letter queue handling for failed messages
[Function("ProcessFailedProgressEvents")]
public async Task ProcessFailedProgressEvents(
    [QueueTrigger("signalr-progress-events-poison")] string failedMessage,
    ILogger logger)
{
    try
    {
        // Attempt to reprocess failed message
        var progressEvent = JsonSerializer.Deserialize<ProgressEvent>(failedMessage);
        
        // Log failure details for analysis
        logger.LogWarning("🔄 [RETRY] Reprocessing failed progress event: {MigrationId}, {EventType}", 
            progressEvent.MigrationId, progressEvent.EventType);
            
        // Send to alternative processing path or manual review queue
        await SendToManualReviewAsync(progressEvent);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "💥 [POISON] Failed to process poison message: {Message}", failedMessage);
    }
}
```

---

## 🔄 Maintenance & Operations

### Regular Maintenance Tasks

#### Daily Monitoring
- **Connection Health**: Monitor active SignalR connections and reconnection rates
- **Queue Metrics**: Check message throughput and queue depth
- **Error Rates**: Review failed connection and message processing rates
- **Performance**: Monitor event delivery latency

#### Weekly Analysis  
- **Capacity Planning**: Analyze SignalR unit consumption and storage usage
- **Performance Optimization**: Review rate limiting effectiveness and adjust intervals
- **Error Pattern Analysis**: Identify recurring connection or processing issues
- **Security Review**: Check for unusual connection patterns or potential attacks

#### Monthly Optimization
- **Configuration Tuning**: Optimize rate limiting intervals and batch sizes
- **Scaling Assessment**: Evaluate need for SignalR service tier upgrades
- **Archive Old Data**: Clean up historical queue and log data
- **Documentation Updates**: Update troubleshooting guides based on recent issues

### Configuration Updates

**Safe Update Process**:
1. **Test in Development**: Validate SignalR configuration changes in dev environment
2. **Staged Deployment**: Deploy to staging environment with production-like load
3. **Monitor Metrics**: Watch connection success rates and event delivery latency
4. **Gradual Rollout**: Use feature flags to gradually enable new functionality
5. **Rollback Plan**: Maintain previous configuration for quick rollback if needed

### Scaling Considerations

**Horizontal Scaling**:
- **Azure Functions**: Automatically scales based on queue message volume
- **SignalR Service**: Choose appropriate tier based on concurrent connection needs
- **Storage Queues**: No scaling needed - automatically handles high throughput

**Vertical Scaling**:
- **SignalR Service Tiers**:
  - Free: 20 connections, 20k messages/day
  - Standard: 1k connections, 1M messages/day  
  - Premium: 100k connections, unlimited messages
- **Azure Functions Plans**:
  - Consumption: Auto-scale, pay-per-execution
  - Premium: Pre-warmed instances, faster cold starts
  - Dedicated: Predictable performance, higher costs

---

## 📚 Reference Materials

### Key Files to Understand

| Component | File Path | Purpose |
|-----------|-----------|---------|
| **Event Factory** | `SignalREventFactory.cs` | Centralized event creation |
| **Message Converter** | `SignalRMessageConverter.cs` | Frontend compatibility |
| **Event Publisher** | `ProgressEventPublisher.cs` | Queue integration |
| **Azure Functions** | `SignalRProgressFunctions.cs` | Queue processing and broadcasting |
| **Frontend Service** | `signalRService.ts` | Client connection management |
| **React Integration** | `useRealTimeMigrationProgress.ts` | Hook for real-time data |

### External Dependencies

- **Azure SignalR Service**: Real-time messaging service
- **Azure Storage Queues**: Reliable message delivery
- **Azure Functions**: Serverless event processing
- **Microsoft SignalR Client**: Frontend JavaScript library

### Further Reading

- [Azure SignalR Service Documentation](https://docs.microsoft.com/azure/azure-signalr/)
- [Azure Functions SignalR Bindings](https://docs.microsoft.com/azure/azure-functions/functions-bindings-signalr-service)
- [ASP.NET Core SignalR Overview](https://docs.microsoft.com/aspnet/core/signalr/introduction)

---

## 💡 Best Practices Summary

1. **Use Centralized Event Factory** for all SignalR event creation
2. **Implement Rate Limiting** to prevent message flooding
3. **Handle Connection Failures Gracefully** with automatic reconnection
4. **Monitor Queue Health** and implement dead letter processing
5. **Use Structured Logging** for debugging and monitoring
6. **Test Multi-Instance Scenarios** to ensure proper scaling
7. **Implement Soft Cancellation** to avoid confusing UI updates
8. **Validate Event Data** on both backend and frontend
9. **Use Feature Flags** for gradual SignalR feature rollouts
10. **Document Configuration Changes** and their impacts

---

*This document should be reviewed and updated whenever significant changes are made to the SignalR real-time status system.*