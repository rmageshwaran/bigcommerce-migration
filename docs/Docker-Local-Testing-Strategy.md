# Azure Functions Local Testing Strategy

This document outlines the Docker-based local testing strategy for the **BigCommerce Migration System** built on **Azure Functions serverless architecture**.

## Table of Contents
1. [Architecture Overview](#architecture-overview)
2. [Docker Compose Setup](#docker-compose-setup)
3. [Local Development Workflow](#local-development-workflow)
4. [Testing Strategies](#testing-strategies)
5. [Monitoring and Debugging](#monitoring-and-debugging)
6. [Configuration Management](#configuration-management)

---

## 🏗️ **Architecture Overview**

### **Correct Azure Functions Architecture**

Our BigCommerce Migration System is built on **Azure Functions serverless architecture**:

```
HTTP Trigger Functions → Durable Functions → Activity Functions → Azure Storage + OpenSearch
```

**Key Components:**
- **Azure Functions Runtime**: HTTP triggers, durable orchestrations, activity functions
- **Azure Storage Emulator (Azurite)**: Tables, Blobs, Queues
- **OpenSearch**: Logging, search, and analytics
- **Mock BigCommerce API**: Testing simulation
- **Application Insights**: Monitoring and telemetry

### **❌ What We DON'T Use:**
- ~~Web API controllers~~ → **HTTP Trigger Functions**
- ~~PostgreSQL database~~ → **Azure Table Storage**
- ~~Grafana/Prometheus~~ → **Application Insights + Azure Monitor**
- ~~Seq logging~~ → **OpenSearch**

---

## 🐳 **Docker Compose Setup**

### **docker-compose.yml**

```yaml
version: '3.8'

services:
  # Azure Functions Runtime
  bigcommerce-migration-functions:
    build:
      context: .
      dockerfile: Dockerfile.functions
    container_name: bigcommerce-migration-functions
    ports:
      - "7071:7071"  # Azure Functions default port
    environment:
      - FUNCTIONS_WORKER_RUNTIME=dotnet
      - AzureWebJobsStorage=DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite:10000/devstoreaccount1;QueueEndpoint=http://azurite:10001/devstoreaccount1;TableEndpoint=http://azurite:10002/devstoreaccount1;
      - OpenSearch__Url=http://opensearch:9200
      - BigCommerce__MockApiUrl=http://mock-bigcommerce:8090
      - APPINSIGHTS_INSTRUMENTATIONKEY=${APPINSIGHTS_INSTRUMENTATIONKEY}
      - APPLICATIONINSIGHTS_CONNECTION_STRING=${APPLICATIONINSIGHTS_CONNECTION_STRING}
    depends_on:
      - azurite
      - opensearch
      - mock-bigcommerce
    volumes:
      - ./src:/app/src
      - ./host.json:/app/host.json
      - ./local.settings.json:/app/local.settings.json
    networks:
      - bigcommerce-migration-network
    restart: unless-stopped

  # Azure Storage Emulator (Azurite)
  azurite:
    image: mcr.microsoft.com/azure-storage/azurite:latest
    container_name: bigcommerce-migration-azurite
    ports:
      - "10000:10000"  # Blob service
      - "10001:10001"  # Queue service
      - "10002:10002"  # Table service
    volumes:
      - azurite_data:/data
    command: ["azurite", "--blobHost", "0.0.0.0", "--queueHost", "0.0.0.0", "--tableHost", "0.0.0.0", "--location", "/data"]
    networks:
      - bigcommerce-migration-network
    restart: unless-stopped

  # OpenSearch (Logging and Analytics)
  opensearch:
    image: opensearchproject/opensearch:2.11.0
    container_name: bigcommerce-migration-opensearch
    ports:
      - "9200:9200"
      - "9300:9300"
    environment:
      - discovery.type=single-node
      - bootstrap.memory_lock=true
      - "OPENSEARCH_JAVA_OPTS=-Xms1g -Xmx1g"
      - "DISABLE_SECURITY_PLUGIN=true"
    volumes:
      - opensearch_data:/usr/share/opensearch/data
    networks:
      - bigcommerce-migration-network
    restart: unless-stopped

  # OpenSearch Dashboards
  opensearch-dashboards:
    image: opensearchproject/opensearch-dashboards:2.11.0
    container_name: bigcommerce-migration-dashboards
    ports:
      - "5601:5601"
    environment:
      - OPENSEARCH_HOSTS=http://opensearch:9200
      - "DISABLE_SECURITY_DASHBOARDS_PLUGIN=true"
    depends_on:
      - opensearch
    networks:
      - bigcommerce-migration-network
    restart: unless-stopped

  # Mock BigCommerce API
  mock-bigcommerce:
    build:
      context: .
      dockerfile: tests/BigCommerce.Migration.MockApi/Dockerfile
    container_name: bigcommerce-migration-mock-api
    ports:
      - "8090:8090"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:8090
    networks:
      - bigcommerce-migration-network
    restart: unless-stopped

  # Integration Tests
  integration-tests:
    build:
      context: .
      dockerfile: tests/BigCommerce.Migration.IntegrationTests/Dockerfile
    environment:
      - ASPNETCORE_ENVIRONMENT=Testing
      - AzureWebJobsStorage=DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite:10000/devstoreaccount1;QueueEndpoint=http://azurite:10001/devstoreaccount1;TableEndpoint=http://azurite:10002/devstoreaccount1;
      - OpenSearch__Url=http://opensearch:9200
      - BigCommerce__MockApiUrl=http://mock-bigcommerce:8090
      - FunctionApp__BaseUrl=http://bigcommerce-migration-functions:7071
    depends_on:
      - azurite
      - opensearch
      - bigcommerce-migration-functions
    networks:
      - bigcommerce-migration-network
    profiles:
      - testing

networks:
  bigcommerce-migration-network:
    driver: bridge

volumes:
  azurite_data:
  opensearch_data:
```

### **Dockerfile.functions**

```dockerfile
FROM mcr.microsoft.com/azure-functions/dotnet:4-dotnet6
WORKDIR /app

# Copy function app files
COPY src/BigCommerce.Migration.Functions/ ./
COPY host.json ./
COPY local.settings.json ./

# Install dependencies
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Copy published output
WORKDIR /app/publish
EXPOSE 7071

# Start Azure Functions runtime
CMD ["func", "host", "start", "--host", "0.0.0.0", "--port", "7071"]
```

### **host.json** (Azure Functions Configuration)

```json
{
  "version": "2.0",
  "logging": {
    "applicationInsights": {
      "samplingSettings": {
        "isEnabled": true,
        "excludedTypes": "Request"
      }
    }
  },
  "extensions": {
    "durableTask": {
      "hubName": "BigCommerceMigrationHub",
      "storageProvider": {
        "connectionStringName": "AzureWebJobsStorage"
      }
    }
  },
  "functionTimeout": "00:05:00",
  "managedDependency": {
    "enabled": true
  }
}
```

### **local.settings.json** (Local Development Settings)

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite:10000/devstoreaccount1;QueueEndpoint=http://azurite:10001/devstoreaccount1;TableEndpoint=http://azurite:10002/devstoreaccount1;",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet",
    "OpenSearch__Url": "http://opensearch:9200",
    "BigCommerce__MockApiUrl": "http://mock-bigcommerce:8090"
  }
}
```

---

## 🚀 **Local Development Workflow**

### **Quick Start Commands**

```bash
# Start all services
docker-compose up -d

# View Azure Functions logs
docker-compose logs -f bigcommerce-migration-functions

# Test HTTP trigger function
curl -X POST http://localhost:7071/api/migrations \
  -H "Content-Type: application/json" \
  -d '{"sourceStoreId": "store-1", "destinationStoreId": "store-2", "entities": ["Products"]}'

# Stop all services
docker-compose down

# Clean up everything
docker-compose down -v --remove-orphans
```

### **Azure Functions Development Commands**

```bash
# Install Azure Functions Core Tools in container
docker-compose exec bigcommerce-migration-functions func --version

# Check function app health
curl -f http://localhost:7071/api/health

# List all functions
docker-compose exec bigcommerce-migration-functions func list

# View function logs
docker-compose exec bigcommerce-migration-functions func logs

# Debug specific function
docker-compose exec bigcommerce-migration-functions func start --debug
```

### **Testing Commands**

```bash
# Run integration tests
docker-compose --profile testing run --rm integration-tests

# Run specific test category
docker-compose --profile testing run --rm integration-tests dotnet test --filter "Category=HttpTrigger"

# Run performance tests
docker-compose --profile testing run --rm integration-tests dotnet test --filter "Category=Performance"
```

---

## 📊 **Monitoring and Debugging**

### **Data Architecture Explanation**

Our BigCommerce Migration System uses a **serverless-first data architecture** aligned with Azure services:

#### **✅ What We Actually Use:**
- **Azure Table Storage**: Entity ID mappings, migration status, rate limiting, configuration
- **Azure Blob Storage**: Large payloads, failed request data, temporary files
- **OpenSearch**: Comprehensive logging, search, and analytics
- **BigCommerce APIs**: External data source (not persistent storage)

#### **❌ Why No Traditional Monitoring:**
- **No Grafana**: We use Application Insights + Azure Monitor for serverless monitoring
- **No Seq**: OpenSearch handles all logging and search requirements
- **No PostgreSQL**: Contradicts serverless scale-to-zero architecture
- **No Prometheus**: Azure Monitor provides metrics for serverless functions

#### **Data Flow Pattern:**
```
HTTP Trigger → Durable Orchestrator → Activity Functions → Azure Storage + OpenSearch
```

### **Service Health Checks**

```bash
# Check Azure Functions runtime
curl -f http://localhost:7071/api/health

# Check OpenSearch
curl -f http://localhost:9200/_cluster/health

# Check Azurite (Azure Storage Emulator)
curl -f http://localhost:10000

# Check Mock BigCommerce API
curl -f http://localhost:8090/health
```

### **Accessing Services**

| **Service** | **URL** | **Purpose** |
|-------------|---------|-------------|
| **Azure Functions** | http://localhost:7071 | HTTP trigger functions |
| **Functions Admin** | http://localhost:7071/admin | Runtime management |
| **OpenSearch** | http://localhost:9200 | Logging and search |
| **OpenSearch Dashboards** | http://localhost:5601 | Search analytics |
| **Mock BigCommerce API** | http://localhost:8090 | API simulation |
| **Azurite Tables** | http://localhost:10002 | Table storage |
| **Azurite Blobs** | http://localhost:10000 | Blob storage |
| **Azurite Queues** | http://localhost:10001 | Queue storage |

### **Function Endpoints**

| **Function** | **Method** | **URL** | **Purpose** |
|-------------|------------|---------|-------------|
| **StartMigration** | POST | /api/migrations | Start new migration |
| **GetStatus** | GET | /api/migrations/{id} | Get migration status |
| **GetAllMigrations** | GET | /api/migrations | List all migrations |
| **GetErrors** | GET | /api/migrations/{id}/errors | Get migration errors |
| **CancelMigration** | POST | /api/migrations/{id}/cancel | Cancel migration |
| **ExportCSV** | GET | /api/migrations/{id}/export | Export results |
| **HealthCheck** | GET | /api/health | System health |

---

## 🧪 **Testing Strategies**

### **HTTP Trigger Testing**

```bash
# Test StartMigration function
curl -X POST http://localhost:7071/api/migrations \
  -H "Content-Type: application/json" \
  -d '{
    "sourceStoreId": "store-abc123",
    "destinationStoreId": "store-xyz789",
    "entities": ["Products", "Categories"]
  }'

# Test GetStatus function
curl -X GET http://localhost:7071/api/migrations/550e8400-e29b-41d4-a716-446655440000

# Test health endpoint
curl -X GET http://localhost:7071/api/health
```

### **Durable Functions Testing**

```bash
# Check orchestration status
curl -X GET http://localhost:7071/runtime/webhooks/durabletask/instances/550e8400-e29b-41d4-a716-446655440000

# Terminate orchestration
curl -X POST http://localhost:7071/runtime/webhooks/durabletask/instances/550e8400-e29b-41d4-a716-446655440000/terminate \
  -H "Content-Type: application/json" \
  -d '{"reason": "Test termination"}'
```

### **Integration Testing**

```csharp
// Example integration test
[Test]
public async Task StartMigration_WithValidRequest_ReturnsSuccessStatus()
{
    // Arrange
    var client = new HttpClient();
    client.BaseAddress = new Uri("http://localhost:7071");
    
    var request = new {
        sourceStoreId = "store-1",
        destinationStoreId = "store-2",
        entities = new[] { "Products" }
    };
    
    // Act
    var response = await client.PostAsJsonAsync("/api/migrations", request);
    
    // Assert
    response.EnsureSuccessStatusCode();
    var result = await response.Content.ReadAsStringAsync();
    Assert.That(result, Does.Contain("migrationId"));
}
```

---

## 🔧 **Configuration Management**

### **Environment Variables**

```bash
# .env file for local development
COMPOSE_PROJECT_NAME=bigcommerce-migration
APPINSIGHTS_INSTRUMENTATIONKEY=your-key-here
APPLICATIONINSIGHTS_CONNECTION_STRING=your-connection-string
```

### **Azure Functions Configuration**

```json
// local.settings.json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite:10000/devstoreaccount1;QueueEndpoint=http://azurite:10001/devstoreaccount1;TableEndpoint=http://azurite:10002/devstoreaccount1;",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet",
    "OpenSearch__Url": "http://opensearch:9200",
    "BigCommerce__MockApiUrl": "http://mock-bigcommerce:8090"
  }
}
```

---

## 🎯 **Summary**

This Docker-based local testing strategy provides:

### **✅ Correct Azure Functions Architecture**
- **HTTP Trigger Functions**: Proper serverless function endpoints
- **Durable Functions**: Long-running orchestrations with state management
- **Activity Functions**: Stateless workers for BigCommerce API calls
- **Azure Storage Emulation**: Complete local storage simulation

### **✅ Serverless-First Design**
- **No Traditional Web API**: Functions-based architecture
- **No SQL Database**: Azure Table Storage for entity mappings
- **No Grafana/Prometheus**: Application Insights for monitoring
- **No Seq**: OpenSearch for logging and analytics

### **✅ Production Parity**
- **Same Runtime**: Azure Functions runtime locally and in cloud
- **Same Storage**: Azurite emulates Azure Storage perfectly
- **Same Monitoring**: OpenSearch matches cloud logging strategy
- **Same Configuration**: Environment variables and settings

### **✅ Developer Experience**
- **One-command setup**: `docker-compose up -d`
- **Function debugging**: Azure Functions debugging tools
- **Real-time logs**: OpenSearch dashboards for log analysis
- **API testing**: HTTP trigger functions with proper endpoints

The complete Docker setup ensures developers can quickly spin up a full Azure Functions environment locally, test serverless architecture patterns, and maintain complete production parity.

---

**Document Status**: Corrected  
**Architecture**: Azure Functions Serverless  
**Last Updated**: January 2025 