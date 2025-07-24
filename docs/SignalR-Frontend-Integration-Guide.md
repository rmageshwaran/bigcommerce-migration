# SignalR Frontend Integration Guide

## 🎯 **Overview**

This guide documents the updated frontend-backend SignalR integration that connects our queue-based backend progress events with the React dashboard.

## ✅ **Phase 3: Frontend-Backend Integration (COMPLETED)**

### **Event Mapping System**

Our SignalR implementation uses a **translation layer** in the frontend to map backend queue events to consistent internal event names.

#### **Backend → Frontend Event Mapping:**

| Backend Queue Event | Frontend Internal Event | Purpose |
|-------------------|------------------------|---------|
| `MigrationProgressUpdated` | `migrationProgress` | Overall migration progress updates |
| `MigrationStatusChanged` | `MigrationStatus` | Migration status changes (started, completed, failed) |
| `EntityProgressUpdated` | `DetailedProgress` + `entityUpdate` | Entity-level progress (categories, products, etc.) |
| `BatchProgressUpdated` | `BatchStarted` + `BatchProgress` + `BatchCompleted` | Batch processing updates |
| `ErrorOccurred` | `error` | Error notifications with details |

#### **Translation Layer Benefits:**
- ✅ **Backward Compatibility**: Frontend hooks use consistent internal names
- ✅ **Flexibility**: Backend can change event names without breaking frontend
- ✅ **Debug Logging**: All events logged for troubleshooting
- ✅ **Type Safety**: Proper TypeScript interfaces maintained

## 🔧 **Technical Implementation**

### **Updated SignalR Service (`signalRService.ts`)**

```typescript
// Backend sends: MigrationProgressUpdated
this.connection.on('MigrationProgressUpdated', (progress: MigrationProgress) => {
  console.log('🎯 DEBUG: Received MigrationProgressUpdated:', progress);
  this.notifyListeners('migrationProgress', progress); // Internal event name
});

// Backend sends: EntityProgressUpdated  
this.connection.on('EntityProgressUpdated', (entityProgress: any) => {
  console.log('🎯 DEBUG: Received EntityProgressUpdated:', entityProgress);
  this.notifyListeners('DetailedProgress', entityProgress);
  this.notifyListeners('entityUpdate', entityProgress); // Additional internal event
});
```

### **Hook Usage (No Changes Required)**

Frontend hooks continue to use the same internal event names:

```typescript
// useMigrationProgress.ts - No changes needed
const progressUnsubscribe = signalRService.on('migrationProgress', handleProgressUpdate);
const statusUnsubscribe = signalRService.on('MigrationStatus', handleStatusUpdate);

// useRealTimeMigrationProgress.ts - No changes needed  
const unsubscribeProgress = signalRService.current.on('migrationProgress', handleProgressUpdate);
const unsubscribeEntityUpdate = signalRService.current.on('entityUpdate', handleEntityUpdate);
```

## 🧪 **Testing Integration**

### **SignalR Integration Test Component**

Navigate to `/test/signalr` in the dashboard to test the integration:

1. **Connect to SignalR**: Verify connection to backend hub
2. **Test Queue Events**: Simulate queue-based event reception
3. **Monitor Event Log**: View all received events with debug info
4. **Check Event Mapping**: Verify backend events map to internal events correctly

### **Expected Event Flow:**

```
Backend Orchestrator
    ↓ publishes
Azure Storage Queue  
    ↓ triggers
SignalRProgressFunctions
    ↓ broadcasts
Azure SignalR Service
    ↓ sends
Frontend SignalR Client
    ↓ translates
Internal Event Handlers
    ↓ updates
React Components
```

### **Debug Logging**

All SignalR events now include debug logging:

```
🎯 DEBUG: Received MigrationProgressUpdated: {migrationId: "test-123", overallProgress: 45.2, ...}
🎯 DEBUG: Received EntityProgressUpdated: {entityType: "categories", processedCount: 150, ...}
🎯 DEBUG: Received BatchProgressUpdated: {batchNumber: 3, totalBatches: 10, ...}
```

## 🚀 **How to Test End-to-End**

### **Prerequisites:**
1. ✅ Backend Azure Functions running (`func start --port 7071`)
2. ✅ Azure SignalR Service configured
3. ✅ Frontend dashboard running (`npm run dev`)

### **Test Steps:**

1. **Start a Migration**:
   ```bash
   # POST to backend migration endpoint
   curl -X POST http://localhost:7071/api/migrations \
     -H "Content-Type: application/json" \
     -d '{"sourceStore": {...}, "destinationStore": {...}, "entities": ["categories"]}'
   ```

2. **Monitor Frontend**:
   - Navigate to `/test/signalr` in dashboard
   - Click "Connect" to establish SignalR connection
   - Watch for queue-based events in the event log

3. **Verify Event Reception**:
   - ✅ `MigrationProgressUpdated` events received and logged
   - ✅ `EntityProgressUpdated` events received and logged  
   - ✅ `BatchProgressUpdated` events received and logged
   - ✅ Progress updates displayed in dashboard components

### **Expected Results:**

```
✅ SignalR connected: Connected with ID: xyz123
✅ Migration progress: EntityProgressUpdated→DetailedProgress: {entityType: "categories", progress: 25.5%}
✅ Batch progress: BatchProgressUpdated→BatchStarted: {batchNumber: 1, totalBatches: 5}
✅ Error handling: ErrorOccurred→error: {message: "API rate limit exceeded", severity: "warning"}
```

## 🔍 **Troubleshooting**

### **Common Issues:**

1. **No Events Received**:
   - Check Azure Functions logs for SignalR broadcasting
   - Verify Azure SignalR connection string configuration
   - Check browser console for SignalR connection errors

2. **Event Name Mismatches**:
   - All backend events should start with queue-based names
   - Frontend debug logs should show event translation
   - Check `signalRService.ts` event handler setup

3. **Connection Issues**:
   - Verify CORS configuration in Azure Functions
   - Check API keys and authentication settings
   - Confirm Azure SignalR Service is running

### **Debug Commands:**

```bash
# Check backend SignalR configuration
grep -r "AzureSignalR" src/BigCommerce.Migration.Functions/

# Test SignalR connection directly
curl http://localhost:7071/api/negotiate

# Monitor backend logs for event publishing
tail -f backend-logs.txt | grep "Publishing.*Event"
```

## 📊 **Performance Considerations**

- **Event Frequency**: Queue-based events reduce load on orchestrators
- **Message Size**: Progress events are lightweight JSON objects
- **Connection Scaling**: Azure SignalR Service handles multiple clients
- **Error Resilience**: Queue-based approach provides retry capabilities

## ✅ **Completion Checklist**

- [x] ✅ Backend queue-based events implemented
- [x] ✅ Frontend event mapping updated
- [x] ✅ Translation layer working correctly
- [x] ✅ Debug logging added for troubleshooting
- [x] ✅ Build and compilation verified
- [x] ✅ Integration test component updated
- [ ] 🔄 End-to-end testing with live migration
- [ ] 🔄 Performance testing with high-frequency events
- [ ] 🔄 Multi-client connection testing

---

**Implementation Date**: January 23, 2025  
**Status**: ✅ **Frontend Integration Complete - Ready for E2E Testing** 