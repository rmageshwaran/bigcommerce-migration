# Azure SignalR Configuration Guide

## Overview
This application uses Azure SignalR Service for real-time migration progress updates. The configuration is designed to work across all environments: local development, Docker, and production.

## Configuration Structure

### Connection String
The Azure SignalR connection string should be configured in the **ConnectionStrings** section:

```json
{
  "ConnectionStrings": {
    "AzureSignalR": "Endpoint=https://your-signalr-service.service.signalr.net;AccessKey=your-access-key;Version=1.0;"
  }
}
```

### Queue Configuration
Progress events are sent through the `signalr-progress-events` queue:

```json
{
  "Values": {
    "SignalRProgressQueueName": "signalr-progress-events"
  }
}
```

## Environment Setup

### 1. Create Azure SignalR Service
```bash
az signalr create \
  --name your-signalr-service \
  --resource-group your-resource-group \
  --sku Standard_S1 \
  --location eastus
```

### 2. Get Connection String
```bash
az signalr key list \
  --name your-signalr-service \
  --resource-group your-resource-group \
  --query primaryConnectionString \
  --output tsv
```

### 3. Configure Connection String

#### Local Development (local.settings.json)
```json
{
  "ConnectionStrings": {
    "AzureSignalR": "Endpoint=https://your-signalr-service.service.signalr.net;AccessKey=your-access-key;Version=1.0;"
  }
}
```

#### Docker (local.settings.docker.json)
```json
{
  "ConnectionStrings": {
    "AzureSignalR": "Endpoint=https://your-signalr-service.service.signalr.net;AccessKey=your-access-key;Version=1.0;"
  }
}
```

#### Production (Azure App Settings)
Set the connection string in Azure Portal:
- **Name**: `ConnectionStrings:AzureSignalR`
- **Value**: `Endpoint=https://your-signalr-service.service.signalr.net;AccessKey=your-access-key;Version=1.0;`

### 4. Environment Variables (Alternative)
For containerized deployments, you can use environment variables:

```bash
# Connection string
ConnectionStrings__AzureSignalR="Endpoint=https://your-signalr-service.service.signalr.net;AccessKey=your-access-key;Version=1.0;"

# Queue configuration  
SignalRProgressQueueName="signalr-progress-events"
```

## SignalR Functions
The following Azure Functions handle SignalR operations:

### 1. SignalRNegotiation
- **URL**: `POST /api/SignalRNegotiation`
- **Purpose**: Provides connection info for SignalR clients

### 2. ProcessProgressEvents  
- **Trigger**: Queue trigger on `signalr-progress-events`
- **Purpose**: Processes progress events and broadcasts to SignalR clients

### 3. OnSignalRConnected/OnSignalRDisconnected
- **Purpose**: Logs client connections for monitoring

## Frontend Integration
Frontend clients should connect to the SignalR hub using:

```typescript
const connection = new HubConnectionBuilder()
  .withUrl('/api')  // Azure Functions SignalR endpoint
  .build();

// Listen to progress events
connection.on('MigrationProgressUpdated', (data) => {
  // Handle migration progress
});

connection.on('BatchProgressUpdated', (data) => {
  // Handle batch progress  
});

connection.on('EntityProgressUpdated', (data) => {
  // Handle entity progress
});

connection.on('ErrorOccurred', (data) => {
  // Handle errors
});

connection.on('MigrationStatusChanged', (data) => {
  // Handle status changes
});
```

## Troubleshooting

### Common Issues

1. **SignalR connection fails**
   - Verify connection string format
   - Check Azure SignalR service is running
   - Ensure CORS is configured in host.json

2. **No progress updates received**
   - Verify queue `signalr-progress-events` exists
   - Check Azure Functions are running
   - Verify progress events are being published to queue

3. **Connection timeout**
   - Check Azure SignalR service plan and limits
   - Verify network connectivity

### Debug Steps

1. **Check Azure SignalR Service**
   ```bash
   az signalr show \
     --name your-signalr-service \
     --resource-group your-resource-group
   ```

2. **Verify Queue Messages**
   - Check Azure Storage Queue Explorer
   - Look for messages in `signalr-progress-events` queue

3. **Function Logs**
   - Monitor Azure Functions logs for SignalR-related errors
   - Check Application Insights for detailed telemetry

## Security Notes

- Store connection strings securely using Azure Key Vault in production
- Use managed identity where possible
- Rotate access keys regularly
- Monitor SignalR service usage and costs

## Architecture Benefits

- **Deterministic**: Orchestrators only publish to queues, never call SignalR directly
- **Scalable**: Queue-based decoupling allows independent scaling
- **Reliable**: Azure SignalR Service provides built-in reliability and scaling
- **Simple**: Direct Azure Functions SignalR bindings, no complex HTTP services 