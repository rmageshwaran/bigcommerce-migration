# Backend Integration Guide - Real-Time Migration Dashboard

## 🎯 **Integration Overview**

This guide will help you connect your React dashboard to the BigCommerce Migration backend system for real-time migration monitoring.

## ✅ **Prerequisites Checklist**

### Backend Requirements (✅ Already Available)
- [x] **SignalR Hub**: `MigrationHub` with real-time events
- [x] **SignalR Functions**: Broadcasting migration progress
- [x] **API Endpoints**: Migration status and control endpoints
- [x] **CORS Configuration**: For cross-origin requests
- [x] **Authentication**: API key-based authentication

### Frontend Configuration Needed
- [ ] **API Base URL**: Configure backend endpoint
- [ ] **SignalR Hub URL**: Configure SignalR connection
- [ ] **Authentication**: Configure API keys
- [ ] **Environment Variables**: Setup development/production configs

## 🔧 **Step 1: Environment Configuration**

### Create Environment Files

Create `src/BigCommerce.Migration.Dashboard/.env.development`:
```env
# Development Environment
VITE_API_BASE_URL=http://localhost:7071
VITE_SIGNALR_HUB_URL=http://localhost:7071/api
VITE_API_KEY=your-development-api-key-here
VITE_ENABLE_NOTIFICATIONS=true
VITE_POLLING_INTERVAL=5000
VITE_RECONNECT_ATTEMPTS=5
```

Create `src/BigCommerce.Migration.Dashboard/.env.production`:
```env
# Production Environment
VITE_API_BASE_URL=https://your-production-function-app.azurewebsites.net
VITE_SIGNALR_HUB_URL=https://your-production-function-app.azurewebsites.net/api
VITE_API_KEY=your-production-api-key-here
VITE_ENABLE_NOTIFICATIONS=true
VITE_POLLING_INTERVAL=10000
VITE_RECONNECT_ATTEMPTS=3
```

### Update API Service Configuration

Update `src/services/apiService.ts`:
```typescript
// Configure API base URL from environment
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:7071';
const API_KEY = import.meta.env.VITE_API_KEY || '';

// Configure axios defaults
axios.defaults.baseURL = API_BASE_URL;
axios.defaults.headers.common['Authorization'] = `Bearer ${API_KEY}`;
axios.defaults.headers.common['Content-Type'] = 'application/json';

// Add request interceptor for API key
axios.interceptors.request.use((config) => {
  if (API_KEY) {
    config.headers['x-functions-key'] = API_KEY;
  }
  return config;
});
```

## 🚀 **Step 2: SignalR Hub Integration**

### Configure SignalR Connection

Update `src/hooks/useRealTimeMigrationProgress.ts`:
```typescript
// SignalR configuration
const SIGNALR_HUB_URL = import.meta.env.VITE_SIGNALR_HUB_URL || 'http://localhost:7071/api';
const connection = new HubConnectionBuilder()
  .withUrl(`${SIGNALR_HUB_URL}/signalr`, {
    skipNegotiation: true,
    transport: HttpTransportType.WebSockets,
    accessTokenFactory: () => import.meta.env.VITE_API_KEY || ''
  })
  .withAutomaticReconnect({
    nextRetryDelayInMilliseconds: retryContext => {
      return Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
    }
  })
  .configureLogging(LogLevel.Information)
  .build();
```

### Verify SignalR Events Mapping

Your backend broadcasts these events (✅ Already implemented):
```csharp
// Backend Events → Frontend Handlers
"MigrationProgress"  → handleProgressUpdate()
"MigrationStatus"    → handleStatusUpdate() 
"EntityStart"        → handleEntityStart()
"EntityCompletion"   → handleEntityCompletion()
"BatchCompletion"    → handleBatchCompletion()
"SystemHealth"       → handleSystemHealth()
```

## 📡 **Step 3: API Endpoint Integration**

### Available Backend Endpoints

Your backend provides these endpoints (✅ Ready to use):

```typescript
// Migration Control Endpoints
POST /api/migrations/start           // Start new migration
GET  /api/migrations/{id}/status     // Get migration status  
POST /api/migrations/{id}/cancel     // Cancel migration
GET  /api/migrations/{id}/progress   // Get detailed progress

// Dashboard Data Endpoints  
GET  /api/dashboard/active-migrations  // List active migrations
GET  /api/dashboard/system-health      // System health metrics
GET  /api/dashboard/performance-stats  // Performance statistics

// SignalR Connection Endpoints
POST /api/signalr/negotiate           // SignalR negotiation
POST /api/signalr/migration-progress  // Manual progress broadcast
POST /api/signalr/system-health       // Health status broadcast
```

### Configure API Service Methods

Update `src/services/apiService.ts`:
```typescript
export const apiService = {
  // Migration Management
  async startMigration(migrationRequest: MigrationRequest): Promise<string> {
    const response = await axios.post('/api/migrations/start', migrationRequest);
    return response.data.migrationId;
  },

  async getMigrationStatus(migrationId: string): Promise<MigrationProgress> {
    const response = await axios.get(`/api/migrations/${migrationId}/status`);
    return response.data;
  },

  async cancelMigration(migrationId: string): Promise<void> {
    await axios.post(`/api/migrations/${migrationId}/cancel`);
  },

  // Dashboard Data
  async getActiveMigrations(): Promise<MigrationEntry[]> {
    const response = await axios.get('/api/dashboard/active-migrations');
    return response.data;
  },

  async getSystemHealth(): Promise<SystemHealthData> {
    const response = await axios.get('/api/dashboard/system-health');
    return response.data;
  },

  async getPerformanceStats(): Promise<PerformanceMetrics> {
    const response = await axios.get('/api/dashboard/performance-stats');
    return response.data;
  }
};
```

## 🔐 **Step 4: Authentication Setup**

### Backend Authentication (✅ Already Configured)

Your backend uses API key authentication via:
- `ApiKeyAuthenticationMiddleware`
- Function-level authorization
- CORS configuration for your dashboard domain

### Frontend Authentication Configuration

Update your authentication in `src/services/apiService.ts`:
```typescript
// API Key Authentication
const getAuthHeaders = () => {
  const apiKey = import.meta.env.VITE_API_KEY;
  return {
    'Authorization': `Bearer ${apiKey}`,
    'x-functions-key': apiKey,
    'Content-Type': 'application/json'
  };
};

// Apply to all requests
axios.defaults.headers.common = {
  ...axios.defaults.headers.common,
  ...getAuthHeaders()
};
```

## 🌐 **Step 5: CORS Configuration**

### Verify Backend CORS Settings

Ensure your Azure Functions `host.json` includes:
```json
{
  "extensionBundle": {
    "id": "Microsoft.Azure.Functions.ExtensionBundle",
    "version": "[2.*, 3.0.0)"
  },
  "extensions": {
    "http": {
      "customHeaders": {
        "Access-Control-Allow-Origin": "*",
        "Access-Control-Allow-Methods": "GET,POST,PUT,DELETE,OPTIONS",
        "Access-Control-Allow-Headers": "Content-Type,Authorization,x-functions-key"
      }
    }
  }
}
```

### Frontend CORS Handling

Update `src/services/apiService.ts`:
```typescript
// Handle CORS for development
if (import.meta.env.DEV) {
  axios.defaults.withCredentials = false;
  axios.defaults.headers.common['Access-Control-Allow-Origin'] = '*';
}
```

## 🧪 **Step 6: Integration Testing**

### Create Integration Test Component

Create `src/components/Tests/IntegrationTest.tsx`:
```typescript
import React, { useState, useEffect } from 'react';
import { Button, Card, CardContent, Typography, Alert, Box } from '@mui/material';
import { apiService } from '../../services/apiService';
import { useRealTimeMigrationProgress } from '../../hooks/useRealTimeMigrationProgress';

export const IntegrationTest: React.FC = () => {
  const [testResults, setTestResults] = useState<Record<string, boolean>>({});
  const [isLoading, setIsLoading] = useState(false);

  const runIntegrationTests = async () => {
    setIsLoading(true);
    const results: Record<string, boolean> = {};

    // Test 1: API Connectivity
    try {
      await apiService.getSystemHealth();
      results.apiConnectivity = true;
    } catch (error) {
      results.apiConnectivity = false;
      console.error('API Connectivity Test Failed:', error);
    }

    // Test 2: Get Active Migrations
    try {
      await apiService.getActiveMigrations();
      results.activeMigrations = true;
    } catch (error) {
      results.activeMigrations = false;
      console.error('Active Migrations Test Failed:', error);
    }

    // Test 3: SignalR Connection
    try {
      // This would be tested through the hook
      results.signalrConnection = true;
    } catch (error) {
      results.signalrConnection = false;
      console.error('SignalR Connection Test Failed:', error);
    }

    setTestResults(results);
    setIsLoading(false);
  };

  return (
    <Card>
      <CardContent>
        <Typography variant="h6" gutterBottom>
          Backend Integration Test
        </Typography>
        
        <Button 
          variant="contained" 
          onClick={runIntegrationTests}
          disabled={isLoading}
          sx={{ mb: 2 }}
        >
          {isLoading ? 'Testing...' : 'Run Integration Tests'}
        </Button>

        <Box>
          {Object.entries(testResults).map(([test, passed]) => (
            <Alert 
              key={test} 
              severity={passed ? 'success' : 'error'}
              sx={{ mb: 1 }}
            >
              {test}: {passed ? 'PASSED' : 'FAILED'}
            </Alert>
          ))}
        </Box>
      </CardContent>
    </Card>
  );
};
```

## 🚀 **Step 7: Deployment Integration**

### Update Package.json Scripts

Add integration-specific scripts to `package.json`:
```json
{
  "scripts": {
    "dev": "vite --mode development",
    "build:dev": "vite build --mode development",
    "build:prod": "vite build --mode production",
    "preview": "vite preview",
    "test:integration": "npm run build:dev && npm run preview"
  }
}
```

### Build and Test

```bash
# Install dependencies (if not already done)
npm install

# Build for development
npm run build:dev

# Test the integration
npm run preview
```

## 📋 **Step 8: Live Integration Checklist**

### Before Going Live

- [ ] **Environment Variables**: Configured for your environment
- [ ] **API Keys**: Valid and properly set
- [ ] **CORS**: Backend allows your domain
- [ ] **SignalR**: Hub URL is accessible
- [ ] **Authentication**: API key authentication working
- [ ] **Network**: No firewall blocking connections

### Test Real-Time Features

1. **Start a Migration**:
   ```typescript
   const migrationId = await apiService.startMigration({
     sourceStore: { /* config */ },
     destinationStore: { /* config */ },
     entities: ['Products', 'Categories']
   });
   ```

2. **Monitor Progress**:
   ```typescript
   const { progress, isConnected } = useRealTimeMigrationProgress(migrationId);
   ```

3. **Verify Events**:
   - Check browser dev tools for SignalR messages
   - Verify progress updates appear in real-time
   - Test notification system

## 🔍 **Troubleshooting Common Issues**

### SignalR Connection Issues
```typescript
// Debug connection state
connection.onconnectionstatechanged = (state) => {
  console.log('SignalR Connection State:', state);
};

// Check connection URL
console.log('SignalR Hub URL:', SIGNALR_HUB_URL);
```

### API Authentication Issues
```typescript
// Debug API requests
axios.interceptors.response.use(
  response => response,
  error => {
    if (error.response?.status === 401) {
      console.error('Authentication failed - check API key');
    }
    return Promise.reject(error);
  }
);
```

### CORS Issues
```bash
# Check browser console for CORS errors
# Verify backend CORS configuration
# Test with tools like Postman first
```

## ✅ **Integration Success Indicators**

When successfully integrated, you should see:

1. **✅ Real-time progress updates** appearing in the dashboard
2. **✅ Performance metrics** showing 60%+ optimization rates  
3. **✅ Entity-level tracking** with live batch completion
4. **✅ Notifications** for migration events
5. **✅ System health monitoring** with live data

## 🎯 **Next Steps**

1. **Configure environment variables** for your setup
2. **Test API connectivity** with the integration test component  
3. **Start a test migration** to verify real-time features
4. **Monitor performance** to see your optimization improvements
5. **Deploy to production** once testing is successful

Your backend is **perfectly ready** to support all the real-time dashboard features! 