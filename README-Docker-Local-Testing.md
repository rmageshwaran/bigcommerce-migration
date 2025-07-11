# 🐳 BigCommerce Migration - Local Docker Testing

## Quick Start (5 minutes)

### 1. **Prerequisites**
- Docker Desktop installed and running
- PowerShell (Windows) or PowerShell Core (Mac/Linux)

### 2. **Start Environment**
```powershell
# Navigate to project root
cd C:\Git\BigCommerce-Migration

# Start all services (first time - builds images)
.\scripts\start-local-docker.ps1 -Build

# Wait for services to be ready (2-3 minutes)
```

### 3. **Test Environment**
```powershell
# Run validation tests
.\scripts\test-local-docker.ps1

# Check specific services
.\scripts\test-local-docker.ps1 -Detailed
```

### 4. **Access Services**
- **🌐 Dashboard**: http://localhost:3000
- **⚡ Functions**: http://localhost:7071/api/health
- **📊 Azurite**: http://localhost:10000/devstoreaccount1

---

## 🎯 What You Get

### **Complete Migration System**
- ✅ **Azure Functions** - Full migration processing engine
- ✅ **React Dashboard** - Real-time monitoring and control
- ✅ **Azurite Storage** - Local Azure Storage emulation
- ✅ **Queue System** - All 7 queues correctly configured
- ✅ **OpenSearch Integration** - Your existing AWS cluster
- ✅ **SignalR** - Real-time updates

### **Perfect for Testing**
- **🚀 Fast** - No Azure deployment delays
- **💰 Free** - No Azure charges during development
- **🔍 Observable** - Full logging and debugging
- **🎛️ Controllable** - Easy start/stop/restart

---

## 📋 Common Commands

```powershell
# Start everything
.\scripts\start-local-docker.ps1

# Start with clean rebuild
.\scripts\start-local-docker.ps1 -Clean -Build

# View logs
.\scripts\start-local-docker.ps1 -Logs

# View specific service logs
.\scripts\start-local-docker.ps1 -Logs -Service bigcommerce-functions

# Stop everything
.\scripts\start-local-docker.ps1 -Stop

# Test environment
.\scripts\test-local-docker.ps1

# Detailed tests
.\scripts\test-local-docker.ps1 -Detailed
```

---

## 🧪 Testing Your Migration

### **1. Basic Health Check**
```bash
curl http://localhost:7071/api/health
```

### **2. Start Test Migration**
```bash
curl -X POST http://localhost:7071/api/migrations/start \
  -H "Content-Type: application/json" \
  -d '{
    "sourceStore": {
      "storeId": "your-source-store",
      "accessToken": "your-access-token",
      "storeUrl": "https://your-store.mybigcommerce.com"
    },
    "destinationStore": {
      "storeId": "your-dest-store",
      "accessToken": "your-dest-token",
      "storeUrl": "https://your-dest.mybigcommerce.com"
    },
    "entityTypes": ["categories"]
  }'
```

### **3. Monitor Progress**
- **Dashboard**: http://localhost:3000
- **Logs**: `.\scripts\start-local-docker.ps1 -Logs -Service bigcommerce-functions`
- **Storage**: Use Azure Storage Explorer with Azurite

---

## 🔧 Configuration

### **Queue Names (All Fixed)**
- `migration-start` - Migration requests
- `entity-batch` - Entity batch processing
- `batch-completion` - Batch completion notifications
- `cancellation` - Cancellation requests
- `progress-update` - Progress updates
- `dead-letter` - Failed messages
- `retry` - Retry processing

### **OpenSearch (Your AWS Cluster)**
- **Endpoint**: `https://search-bigcommerceinsights-co3k7jb4ukk565b4c3bskzucpm.us-east-1.es.amazonaws.com`
- **Index**: `migration-local-docker`
- **Credentials**: Already configured

### **Processing Settings**
- **BatchSize**: 10 (smaller for testing)
- **MaxConcurrency**: 3 (conservative for local)
- **RateLimit**: 12 requests/second

---

## 🚨 Troubleshooting

### **Services Won't Start**
```powershell
# Check Docker is running
docker version

# Check ports aren't in use
netstat -ano | findstr "7071 3000 10000"

# Restart with clean build
.\scripts\start-local-docker.ps1 -Stop -Clean
.\scripts\start-local-docker.ps1 -Build
```

### **Tests Failing**
```powershell
# Check specific service logs
.\scripts\start-local-docker.ps1 -Logs -Service bigcommerce-functions

# Restart specific service
docker-compose restart bigcommerce-functions

# Full environment test
.\scripts\test-local-docker.ps1 -Detailed
```

### **OpenSearch Issues**
```bash
# Test connectivity directly
curl -u admin:VIQInsights@123 \
  "https://search-bigcommerceinsights-co3k7jb4ukk565b4c3bskzucpm.us-east-1.es.amazonaws.com/_cluster/health"
```

---

## 📚 Full Documentation

- **Complete Guide**: `docs/Docker-Local-Testing-Guide.md`
- **Architecture**: `docs/Architecture-Documentation.md`
- **Testing Strategy**: `docs/Testing-Strategy-Guide.md`

---

## 🎉 Ready to Deploy?

Once your local testing is complete and everything works perfectly:

1. **✅ All tests passing** - `.\scripts\test-local-docker.ps1`
2. **✅ Migration workflow working** - Complete end-to-end test
3. **✅ Real-time updates functional** - Dashboard shows progress
4. **✅ Error handling validated** - Test failure scenarios

Then deploy to Azure with confidence! 🚀

---

**Your local Docker environment is production-ready testing at its finest!** 