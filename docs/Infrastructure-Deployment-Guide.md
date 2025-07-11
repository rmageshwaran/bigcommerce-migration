# BigCommerce Migration System - Infrastructure Deployment Guide

## 📋 Overview

This guide provides step-by-step instructions for deploying the BigCommerce Migration System infrastructure across different environments, from local development to production Azure cloud.

## 🎯 Infrastructure Strategy

### **Multi-Layer Deployment Approach**

| Environment | Purpose | Components | Cost | Complexity |
|-------------|---------|------------|------|------------|
| **Local Dev** | Development & Unit Testing | Docker Compose | $0 | Low |
| **Staging** | Integration Testing | Azure (Basic SKUs) | ~$200/month | Medium |
| **Production** | Live Migrations | Azure (Premium SKUs) | ~$800/month | High |

---

## 🛠️ Phase 1: Local Development Infrastructure

### **✅ Ready for Immediate Use**

**Components Available:**
- ✅ Docker Compose configuration
- ✅ Azure Storage emulator (Azurite)
- ✅ OpenSearch for logging
- ✅ Redis for caching
- ✅ PostgreSQL for state management
- ✅ Real-time dashboard

### **Quick Start - Enhanced Local Environment**

```bash
# 1. Start the complete local infrastructure
docker-compose -f docker-compose.enhanced.yml up -d

# 2. Wait for all services to be healthy
docker-compose -f docker-compose.enhanced.yml ps

# 3. Access the services
# - Migration API: http://localhost:7071
# - Dashboard: http://localhost:3000
# - OpenSearch: http://localhost:9200
# - OpenSearch Dashboards: http://localhost:5601
# - Redis: localhost:6379
# - PostgreSQL: localhost:5432
```

### **Service Overview**

| Service | Port | Purpose | Health Check |
|---------|------|---------|--------------|
| **Azure Functions** | 7071 | Migration API | `curl http://localhost:7071/api/health` |
| **OpenSearch** | 9200 | Migration logging | `curl http://localhost:9200/_cluster/health` |
| **OpenSearch Dashboards** | 5601 | Analytics UI | `curl http://localhost:5601/api/status` |
| **Redis** | 6379 | Caching/Rate limiting | `redis-cli ping` |
| **PostgreSQL** | 5432 | State management | `pg_isready -h localhost -p 5432` |
| **React Dashboard** | 3000 | Real-time monitoring | `curl http://localhost:3000` |
| **Azurite** | 10000-10002 | Storage emulator | `curl http://localhost:10000/devstoreaccount1` |

### **Local Testing with Real BigCommerce APIs**

```bash
# Set your BigCommerce sandbox credentials
export BIGCOMMERCE_SOURCE_STORE_HASH="your-source-store-hash"
export BIGCOMMERCE_SOURCE_ACCESS_TOKEN="your-source-access-token"
export BIGCOMMERCE_DEST_STORE_HASH="your-dest-store-hash"
export BIGCOMMERCE_DEST_ACCESS_TOKEN="your-dest-access-token"

# Run end-to-end category migration test
curl -X POST "http://localhost:7071/api/migrations/start" \
  -H "Content-Type: application/json" \
  -d '{
    "sourceStore": {
      "storeHash": "'$BIGCOMMERCE_SOURCE_STORE_HASH'",
      "accessToken": "'$BIGCOMMERCE_SOURCE_ACCESS_TOKEN'"
    },
    "destinationStore": {
      "storeHash": "'$BIGCOMMERCE_DEST_STORE_HASH'",
      "accessToken": "'$BIGCOMMERCE_DEST_ACCESS_TOKEN'"
    },
    "entities": ["categories"],
    "migrationMode": "Test"
  }'
```

---

## 🚀 Phase 2: Azure Cloud Infrastructure

### **Staging Environment Deployment**

#### **Prerequisites**
```bash
# 1. Install Azure CLI
curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash

# 2. Install Terraform
wget -O- https://apt.releases.hashicorp.com/gpg | sudo gpg --dearmor -o /usr/share/keyrings/hashicorp-archive-keyring.gpg
echo "deb [signed-by=/usr/share/keyrings/hashicorp-archive-keyring.gpg] https://apt.releases.hashicorp.com $(lsb_release -cs) main" | sudo tee /etc/apt/sources.list.d/hashicorp.list
sudo apt update && sudo apt install terraform

# 3. Login to Azure
az login
```

#### **Deploy Staging Infrastructure**

```bash
# 1. Navigate to infrastructure directory
cd iac/

# 2. Initialize Terraform
terraform init

# 3. Create staging workspace
terraform workspace new staging

# 4. Plan deployment
terraform plan -var-file="staging.tfvars"

# 5. Deploy infrastructure
terraform apply -var-file="staging.tfvars"
```

#### **Staging Configuration (staging.tfvars)**

```hcl
# Environment Configuration
environment = "staging"
location = "East US"
prefix = "bcmigstg"
resource_group_name = "bigcommerce-migration-staging-rg"

# Credentials (use Azure Key Vault or environment variables)
postgres_admin_password = "SecurePassword123!"
alert_email = "your-team@company.com"

# CORS Configuration
allowed_origins = [
  "https://localhost:3000",
  "https://bcmigstg-migration-dashboard.azurestaticapps.net"
]

# Common Tags
common_tags = {
  Environment = "staging"
  Project     = "BigCommerce-Migration"
  Team        = "Integration"
  ManagedBy   = "Terraform"
}
```

### **Production Environment Deployment**

#### **Production Configuration (production.tfvars)**

```hcl
# Environment Configuration
environment = "prod"
location = "East US"
prefix = "bcmigprod"
resource_group_name = "bigcommerce-migration-prod-rg"

# Credentials (stored in Azure Key Vault)
postgres_admin_password = var.postgres_admin_password
alert_email = "production-alerts@company.com"

# CORS Configuration
allowed_origins = [
  "https://your-production-domain.com",
  "https://bcmigprod-migration-dashboard.azurestaticapps.net"
]

# Common Tags
common_tags = {
  Environment = "production"
  Project     = "BigCommerce-Migration"
  Team        = "Production"
  ManagedBy   = "Terraform"
  CostCenter  = "IT-Infrastructure"
}
```

#### **Production Deployment Commands**

```bash
# 1. Create production workspace
terraform workspace new production

# 2. Plan production deployment
terraform plan -var-file="production.tfvars"

# 3. Deploy production infrastructure
terraform apply -var-file="production.tfvars"

# 4. Configure monitoring and alerting
az monitor log-analytics workspace create \
  --resource-group bigcommerce-migration-prod-rg \
  --workspace-name bigcommerce-migration-prod-workspace
```

---

## 📊 Infrastructure Components Deep Dive

### **Azure Resources Created**

| Resource Type | Purpose | Staging SKU | Production SKU |
|---------------|---------|-------------|----------------|
| **Azure Functions** | Migration orchestration | EP1 | EP2 |
| **Azure Storage** | Queues, tables, blobs | Standard_LRS | Standard_GRS |
| **Azure Search** | Migration analytics | Basic | Standard |
| **PostgreSQL** | State management | GP_Standard_D2s_v3 | GP_Standard_D4s_v3 |
| **Redis Cache** | Rate limiting | Standard_C1 | Premium_P2 |
| **Key Vault** | Secrets management | Standard | Standard |
| **App Insights** | Monitoring | Basic | Standard |
| **SignalR** | Real-time updates | Free_F1 | Standard_S1 |
| **Static Web App** | Dashboard hosting | Free | Standard |

### **Monitoring & Alerting**

**Automated Alerts:**
- Function execution failures > 10/15min
- API response time > 5 seconds
- Queue message processing delays
- Storage account errors
- Database connection issues

**Monitoring Dashboards:**
- Real-time migration progress
- Performance metrics
- Error rate tracking
- Cost analysis
- Resource utilization

---

## 🔧 Configuration Management

### **Environment Variables Management**

#### **Local Development**
```bash
# .env file for local development
BIGCOMMERCE_SOURCE_STORE_HASH=your-source-store
BIGCOMMERCE_SOURCE_ACCESS_TOKEN=your-source-token
BIGCOMMERCE_DEST_STORE_HASH=your-dest-store
BIGCOMMERCE_DEST_ACCESS_TOKEN=your-dest-token
OPENSEARCH_ENDPOINT=http://localhost:9200
REDIS_CONNECTION_STRING=localhost:6379
POSTGRES_CONNECTION_STRING=Host=localhost;Database=bigcommerce_migration;Username=migration_user;Password=migration_password_123
```

#### **Azure Production**
```bash
# Stored in Azure Key Vault
az keyvault secret set --vault-name "bcmigprod-migration-kv" --name "bigcommerce-source-token" --value "your-source-token"
az keyvault secret set --vault-name "bcmigprod-migration-kv" --name "bigcommerce-dest-token" --value "your-dest-token"
```

### **BigCommerce API Configuration**

#### **Required BigCommerce Scopes**
```json
{
  "scope": [
    "store_v2_products",
    "store_v2_products_read_only",
    "store_v2_information",
    "store_v2_information_read_only",
    "store_catalog",
    "store_catalog_read_only"
  ]
}
```

---

## 🧪 End-to-End Testing Strategy

### **Test Environment Setup**

#### **1. Local Integration Tests**
```bash
# Start local infrastructure
docker-compose -f docker-compose.enhanced.yml up -d

# Run integration tests
dotnet test tests/BigCommerce.Migration.IntegrationTests/ --verbosity normal
```

#### **2. Staging Environment Tests**
```bash
# Deploy to staging
terraform apply -var-file="staging.tfvars"

# Run end-to-end tests against staging
curl -X POST "https://bcmigstg-migration-functions.azurewebsites.net/api/migrations/start" \
  -H "Content-Type: application/json" \
  -d @test-migration-request.json
```

#### **3. Production Validation**
```bash
# Deploy to production
terraform apply -var-file="production.tfvars"

# Run smoke tests
curl -X GET "https://bcmigprod-migration-functions.azurewebsites.net/api/health"
```

### **Test Data Management**

#### **BigCommerce Sandbox Setup**
1. Create source and destination BigCommerce sandbox stores
2. Populate source store with test data:
   - 100 categories (with hierarchical structure)
   - 500 products
   - 50 brands
   - Various product variants and images

#### **Test Scenarios**
- **Small Migration**: 10 categories, 50 products
- **Medium Migration**: 100 categories, 500 products
- **Large Migration**: 1000 categories, 5000 products
- **Complex Migration**: Multi-level categories, variants, images

---

## 💰 Cost Optimization

### **Cost Breakdown by Environment**

#### **Local Development**: $0/month
- All services run in Docker containers
- No Azure costs during development

#### **Staging Environment**: ~$200/month
- Azure Functions: $50/month
- PostgreSQL: $80/month
- Redis: $30/month
- Storage: $20/month
- Other services: $20/month

#### **Production Environment**: ~$800/month
- Azure Functions: $200/month
- PostgreSQL: $300/month
- Redis: $150/month
- Storage: $100/month
- Other services: $50/month

### **Cost Optimization Strategies**

1. **Auto-scaling**: Functions scale to zero when idle
2. **Reserved Instances**: 30% savings on PostgreSQL
3. **Lifecycle Management**: Automatic log retention policies
4. **Resource Tagging**: Cost allocation and tracking
5. **Monitoring**: Alerts for cost anomalies

---

## 🚀 Next Steps

### **Immediate Actions (Ready Now)**
1. ✅ **Start Local Development**: Use `docker-compose.enhanced.yml`
2. ✅ **Test Category Migration**: Use real BigCommerce sandbox APIs
3. ✅ **Monitor with OpenSearch**: View migration logs and analytics

### **Short-term Actions (Within 1-2 weeks)**
1. 🔄 **Deploy Staging**: Use Terraform for staging environment
2. 🔄 **Integration Testing**: End-to-end testing with real APIs
3. 🔄 **Dashboard Integration**: Real-time monitoring setup

### **Long-term Actions (Within 1 month)**
1. 📅 **Production Deployment**: Full production infrastructure
2. 📅 **Performance Testing**: Load testing with large datasets
3. 📅 **Security Audit**: Security assessment and compliance

---

## 📞 Support & Troubleshooting

### **Common Issues**

#### **Docker Compose Issues**
```bash
# Check all services status
docker-compose -f docker-compose.enhanced.yml ps

# View logs for specific service
docker-compose -f docker-compose.enhanced.yml logs opensearch

# Restart specific service
docker-compose -f docker-compose.enhanced.yml restart bigcommerce-functions
```

#### **Azure Deployment Issues**
```bash
# Check Terraform state
terraform show

# Debug Azure resources
az resource list --resource-group bigcommerce-migration-staging-rg

# Check Function App logs
az functionapp log tail --name bcmigstg-migration-functions --resource-group bigcommerce-migration-staging-rg
```

### **Monitoring & Alerts**
- **Health Checks**: All services have health endpoints
- **Alerting**: Automated alerts for failures
- **Logs**: Centralized logging in OpenSearch
- **Metrics**: Performance metrics in Application Insights

---

## 📝 Summary

### **✅ What's Available Now**
1. **Complete Local Infrastructure**: Docker Compose with all services
2. **OpenSearch Integration**: Comprehensive migration logging
3. **Real BigCommerce APIs**: Category migration with real API calls
4. **Monitoring Dashboard**: Real-time migration tracking
5. **Azure Infrastructure**: Production-ready Terraform configuration

### **🎯 Ready for End-to-End Testing**
- **Local Testing**: Immediate use with Docker Compose
- **Staging Testing**: 1-2 days deployment time
- **Production Testing**: 3-5 days deployment time (including security review)

The infrastructure is designed to be **production-ready** and **enterprise-grade**, with proper monitoring, alerting, and cost optimization built in from the start. 