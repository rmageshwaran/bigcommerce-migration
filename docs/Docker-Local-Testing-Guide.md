# Docker Local Testing Guide

## 📋 Overview

This guide helps you set up and run the complete BigCommerce Migration System locally using Docker. This is perfect for development, testing, and validation before deploying to Azure.

## 🏗️ Local Architecture

### **Services Included:**
- **Azure Functions** → Migration processing engine (localhost:7071)
- **React Dashboard** → Real-time migration monitoring (localhost:3000)
- **Azurite** → Azure Storage emulator (Blob, Queue, Table storage)
- **SignalR Emulator** → Real-time updates (localhost:8080)
- **External OpenSearch** → Your existing AWS OpenSearch cluster

### **Why Docker Local Testing:**
- ✅ **Faster development** - No Azure deployment delays
- ✅ **Cost-effective** - No Azure charges during testing
- ✅ **Complete environment** - All services working together
- ✅ **Easy debugging** - Local logs and debugging support
- ✅ **Offline development** - Work without internet (except BigCommerce API)

---

## 🚀 Quick Start

### **Prerequisites:**
```bash
# Required software
- Docker Desktop (latest version)
- PowerShell 5.0+ (Windows) or PowerShell Core (Linux/Mac)

# Optional but recommended
- Postman or curl for API testing
- Azure Storage Explorer for viewing Azurite data
```

### **1. Start Everything:**
```powershell
# From project root directory
.\scripts\start-local-docker.ps1

# Or with rebuild
.\scripts\start-local-docker.ps1 -Build
```

### **2. Verify Services:**
```bash
# Check health endpoints
curl http://localhost:7071/api/health
curl http://localhost:3000

# Open dashboard
open http://localhost:3000
```

### **3. Stop Everything:**
```powershell
# Stop services
.\scripts\start-local-docker.ps1 -Stop

# Stop and clean everything
.\scripts\start-local-docker.ps1 -Stop -Clean
```

---

## 📊 Service Details

### **🔧 Azure Functions (Port 7071)**
**Purpose**: Migration processing engine
**Health Check**: `http://localhost:7071/api/health`
**API Endpoints**:
- `GET /api/health` - System health status
- `POST /api/migrations/start` - Start new migration
- `GET /api/migrations/{id}/status` - Get migration status
- `POST /api/migrations/{id}/cancel` - Cancel migration

**Configuration**:
- Uses Azurite for local Azure Storage
- Connects to your AWS OpenSearch cluster
- All queue names match production configuration
- Debug logging enabled

### **🎨 React Dashboard (Port 3000)**
**Purpose**: Real-time migration monitoring
**URL**: `http://localhost:3000`
**Features**:
- Real-time migration progress
- System health monitoring
- Migration control (start/cancel)
- Queue status visualization
- Hot reload for development

### **📦 Azurite Storage Emulator**
**Purpose**: Local Azure Storage replacement
**Ports**:
- `10000` - Blob storage
- `10001` - Queue storage
- `10002` - Table storage

**Access**:
- Connection string: `DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;...`
- Use Azure Storage Explorer with Azurite connection
- Data persists in Docker volume

### **📡 SignalR Emulator (Port 8080)**
**Purpose**: Real-time updates for dashboard
**Features**:
- Progress notifications
- Status updates
- Error notifications

---

## 🔧 Configuration

### **Environment Variables (Automatically Set)**

#### **Queue Names (Fixed)**:
```yaml
MigrationStartQueueName: "migration-start"
EntityBatchQueueName: "entity-batch"
BatchCompletionQueueName: "batch-completion"
CancellationQueueName: "cancellation"
ProgressUpdateQueueName: "progress-update"
DeadLetterQueueName: "dead-letter"
RetryQueueName: "retry"
```

#### **OpenSearch (Your AWS Cluster)**:
```yaml
Endpoint: "https://search-bigcommerceinsights-co3k7jb4ukk565b4c3bskzucpm.us-east-1.es.amazonaws.com"
Username: "admin"
Password: "VIQInsights@123"
DefaultIndex: "migration-local-docker"
```

#### **BigCommerce API**:
```yaml
BaseUrl: "https://api.bigcommerce.com"
RateLimitRequestsPerSecond: 12
UserAgent: "BigCommerce-Migration-System/1.0-Docker"
EnableDebugLogging: true
```

#### **Processing Settings**:
```yaml
BatchSize: 10 (smaller for testing)
MaxConcurrency: 3 (conservative for local)
RetryAttempts: 3
```

### **Custom Configuration**

To override settings, edit `src/BigCommerce.Migration.Functions/local.settings.docker.json`:

```json
{
  "BigCommerce": {
    "RateLimitRequestsPerSecond": 5,
    "EnableDebugLogging": true
  },
  "AppSettings": {
    "BatchSize": 5
  }
}
```

---

## 🧪 Testing Workflows

### **1. Basic Health Check**
```bash
# Test Functions health
curl http://localhost:7071/api/health

# Expected response
{
  "status": "healthy",
  "services": {
    "opensearch": "healthy",
    "storage": "healthy",
    "signalr": "healthy"
  }
}
```

### **2. Queue System Test**
```bash
# Start a test migration
curl -X POST http://localhost:7071/api/migrations/start \
  -H "Content-Type: application/json" \
  -d '{
    "sourceStore": {
      "storeId": "test-source",
      "accessToken": "your-token",
      "storeUrl": "https://your-store.mybigcommerce.com"
    },
    "destinationStore": {
      "storeId": "test-dest", 
      "accessToken": "your-token",
      "storeUrl": "https://your-dest-store.mybigcommerce.com"
    },
    "entityTypes": ["categories"]
  }'
```

### **3. Dashboard Testing**
1. **Open Dashboard**: `http://localhost:3000`
2. **Check Real-time Updates**: Start a migration and watch progress
3. **Test Controls**: Use start/cancel buttons
4. **Monitor Queues**: View queue status and metrics

### **4. Storage Testing**
```bash
# Using Azure Storage Explorer
# Connection: Local Storage Emulator
# Account: devstoreaccount1

# View queues created by migration
# Check table storage for migration state
# Monitor blob storage for any file uploads
```

### **5. OpenSearch Testing**
```bash
# Check OpenSearch connectivity
curl -u admin:VIQInsights@123 \
  "https://search-bigcommerceinsights-co3k7jb4ukk565b4c3bskzucpm.us-east-1.es.amazonaws.com/_cluster/health"

# View migration logs
curl -u admin:VIQInsights@123 \
  "https://search-bigcommerceinsights-co3k7jb4ukk565b4c3bskzucpm.us-east-1.es.amazonaws.com/migration-local-docker/_search"
```

---

## 📋 Useful Commands

### **Service Management**
```powershell
# Start all services
.\scripts\start-local-docker.ps1

# Start with rebuild
.\scripts\start-local-docker.ps1 -Build

# Clean restart (remove volumes)
.\scripts\start-local-docker.ps1 -Clean -Build

# Stop all services
.\scripts\start-local-docker.ps1 -Stop

# Stop and clean everything
.\scripts\start-local-docker.ps1 -Stop -Clean
```

### **Logging & Debugging**
```powershell
# View all logs
.\scripts\start-local-docker.ps1 -Logs

# View specific service logs
.\scripts\start-local-docker.ps1 -Logs -Service bigcommerce-functions
.\scripts\start-local-docker.ps1 -Logs -Service bigcommerce-dashboard
.\scripts\start-local-docker.ps1 -Logs -Service azurite

# Follow logs in real-time
docker-compose logs -f bigcommerce-functions
```

### **Container Management**
```bash
# View running containers
docker ps

# Execute commands in container
docker exec -it bigcommerce-functions bash

# View container details
docker inspect bigcommerce-functions

# Restart specific service
docker-compose restart bigcommerce-functions
```

### **Storage Management**
```bash
# View Azurite data
docker volume inspect bigcommerce-migration_azurite-data

# Clear Azurite data
docker-compose down -v
docker volume rm bigcommerce-migration_azurite-data
```

---

## 🔧 Troubleshooting

### **Common Issues**

#### **1. Functions Not Starting**
```bash
# Check logs
docker-compose logs bigcommerce-functions

# Common causes:
- Azurite not ready
- Port 7071 already in use
- Missing environment variables
- Build failures

# Solutions:
docker-compose restart azurite
netstat -ano | findstr 7071  # Check port usage
.\scripts\start-local-docker.ps1 -Build  # Rebuild
```

#### **2. Dashboard Not Loading**
```bash
# Check logs
docker-compose logs bigcommerce-dashboard

# Common causes:
- Node modules not installed
- Port 3000 already in use
- API connection issues

# Solutions:
docker-compose restart bigcommerce-dashboard
netstat -ano | findstr 3000  # Check port usage
```

#### **3. OpenSearch Connection Issues**
```bash
# Test connectivity outside Docker
curl -u admin:VIQInsights@123 \
  "https://search-bigcommerceinsights-co3k7jb4ukk565b4c3bskzucpm.us-east-1.es.amazonaws.com/_cluster/health"

# Check Functions environment
docker exec -it bigcommerce-functions env | grep OpenSearch
```

#### **4. Queue Messages Not Processing**
```bash
# Check Azurite status
curl http://localhost:10001/devstoreaccount1

# Check queue creation
# Use Azure Storage Explorer to verify queues exist

# Check Functions logs for queue errors
docker-compose logs bigcommerce-functions | grep -i queue
```

### **Debug Mode**
```bash
# Enable verbose logging
# Edit local.settings.docker.json:
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "BigCommerce.Migration": "Trace"
    }
  }
}

# Restart Functions
docker-compose restart bigcommerce-functions
```

### **Performance Issues**
```bash
# Check Docker resource usage
docker stats

# Adjust Docker Desktop resources:
# Settings → Resources → Advanced
# Increase Memory to 8GB+
# Increase CPU to 4+ cores

# Reduce batch sizes for testing
# Edit local.settings.docker.json:
{
  "AppSettings": {
    "BatchSize": 5,
    "MaxConcurrency": 2
  }
}
```

---

## 🎯 Testing Scenarios

### **Scenario 1: Basic Migration Test**
1. **Start Environment**: `.\scripts\start-local-docker.ps1`
2. **Verify Health**: Check all endpoints return 200
3. **Start Migration**: Use dashboard or API
4. **Monitor Progress**: Watch real-time updates
5. **Check Logs**: Verify OpenSearch logs created

### **Scenario 2: Cancellation Test**
1. **Start Long Migration**: Large store with many entities
2. **Cancel Mid-Process**: Use dashboard cancel button
3. **Verify Cleanup**: Check queues are cleared
4. **Check State**: Verify cancellation tokens in table storage

### **Scenario 3: Error Handling Test**
1. **Use Invalid Credentials**: Trigger BigCommerce API errors
2. **Monitor Behavior**: Verify continue-on-failure works
3. **Check Error Logs**: Verify proper error tracking
4. **Test Recovery**: Fix credentials and retry

### **Scenario 4: Performance Test**
1. **Configure Small Batches**: BatchSize = 5
2. **Monitor Resource Usage**: Docker stats
3. **Test Scaling**: Increase MaxConcurrency
4. **Measure Throughput**: Track entities per minute

---

## 📈 Next Steps

### **Development Workflow**
1. **Code Changes**: Edit source code locally
2. **Rebuild**: `.\scripts\start-local-docker.ps1 -Build`
3. **Test**: Use dashboard and API testing
4. **Debug**: View logs and troubleshoot
5. **Iterate**: Repeat until satisfied

### **Before Azure Deployment**
1. **Validate Complete E2E**: All migration types work
2. **Test Edge Cases**: Error conditions, cancellation
3. **Performance Verification**: Acceptable throughput
4. **Log Validation**: OpenSearch integration working

### **Production Readiness Checklist**
- [ ] ✅ All health checks pass
- [ ] ✅ Migration workflow completes successfully  
- [ ] ✅ Real-time progress updates working
- [ ] ✅ Cancellation system functional
- [ ] ✅ Error handling and logging operational
- [ ] ✅ Queue system processing correctly
- [ ] ✅ OpenSearch integration validated

---

**Your local Docker environment provides a complete, production-like testing environment that validates your entire BigCommerce Migration System before any Azure deployment!** 🚀 