# BigCommerce Migration - Terraform Configuration

## 📋 Overview

This directory contains the Terraform configuration for deploying the BigCommerce Migration System to Azure. The configuration has been fixed and updated to support both development and production environments.

## 🔧 Fixed Issues

### **Key Problems Resolved:**

1. **Missing Variable Definitions** - Added all required variables to `variables.tf`
2. **Removed Unnecessary Resources** - Removed Redis, PostgreSQL, and Azure Search (not needed)
3. **External OpenSearch Integration** - Configured for existing AWS OpenSearch cluster
4. **SignalR Configuration** - Fixed service mode and features block
5. **Static Web App** - Updated SKU configuration
6. **Key Vault Dependencies** - Added proper dependency management
7. **Simplified Architecture** - Aligned with actual system design using Azure Table Storage and external OpenSearch

## 📁 File Structure

```
iac/
├── main.production.tf           # 🔧 FIXED - Complete production infrastructure
├── main.tf                     # Simple existing infrastructure (legacy)
├── variables.tf                # 🔧 FIXED - All variable definitions
├── terraform.dev.tfvars        # 🔧 FIXED - Development environment values
├── terraform.prod.tfvars       # 🔧 FIXED - Production environment values
├── validate-terraform.ps1      # 🆕 NEW - Validation script
├── backend.tf                  # Backend configuration
├── provider.tf                 # Provider configuration
└── README-Production-Terraform.md  # This file
```

## 🚀 Resources Deployed

### **Core Infrastructure:**
- **Resource Group** - Container for all resources
- **Storage Account** - Function app storage and data persistence
- **Key Vault** - Secure secrets management
- **Log Analytics Workspace** - Centralized logging
- **Application Insights** - Application monitoring

### **Compute & Processing:**
- **Azure Functions** - Serverless migration processing
- **Service Plan** - Hosting plan for Functions
- **Note**: Redis and PostgreSQL removed - system uses Azure Table Storage for all state management

### **Frontend & API:**
- **Static Web App** - Migration dashboard hosting
- **SignalR Service** - Real-time progress updates
- **External OpenSearch** - AWS OpenSearch cluster (existing infrastructure)

### **Monitoring & Alerts:**
- **Monitor Action Group** - Alert notifications
- **Metric Alerts** - Function error monitoring

## 🔐 Security Configuration

### **Key Vault Integration:**
- Secrets stored securely in Azure Key Vault
- Function app has managed identity access
- OpenSearch endpoint and credentials stored as secrets

### **Network Security:**
- CORS configured for allowed origins
- HTTPS only for all services
- Minimum TLS 1.2 enforced
- Azure Table Storage encrypted at rest

## 📊 Environment Configurations

### **Development Environment:**
```bash
# Optimized for cost and testing
Storage: Azure Table Storage (Standard LRS)
SignalR: Free_F1
Static Web App: Free tier
OpenSearch: External AWS cluster (existing)
Functions: Consumption Plan
```

### **Production Environment:**
```bash
# Optimized for performance and reliability
Storage: Azure Table Storage (Standard GRS)
SignalR: Standard_S1 (2 capacity)
Static Web App: Standard tier
OpenSearch: External AWS cluster (existing)
Functions: Premium Plan
```

## 🚦 Usage Instructions

### **1. Prerequisites:**
```bash
# Install Terraform
winget install HashiCorp.Terraform

# Install Azure CLI
winget install Microsoft.AzureCLI

# Login to Azure
az login
```

### **2. Configure Environment Variables:**
```bash
# Edit the appropriate .tfvars file
# IMPORTANT: Update sensitive values!

# For development:
notepad terraform.dev.tfvars

# For production:
notepad terraform.prod.tfvars
```

### **3. Run Validation Script:**
```powershell
# Validate development environment
.\validate-terraform.ps1 -Environment dev

# Validate production environment
.\validate-terraform.ps1 -Environment prod
```

### **4. Deploy Infrastructure:**
```bash
# Initialize Terraform
terraform init -backend-config="config_dev.azurerm.tfbackend"

# Plan deployment
terraform plan -var-file="terraform.dev.tfvars"

# Apply changes
terraform apply -var-file="terraform.dev.tfvars"
```

## 🔴 CRITICAL SECURITY UPDATES REQUIRED

### **Before Deployment:**

1. **Update Alert Email:**
   ```bash
   # In terraform.dev.tfvars and terraform.prod.tfvars
   alert_email = "your-alerts@yourcompany.com"
   ```

2. **Update Allowed Origins:**
   ```bash
   # In terraform.dev.tfvars and terraform.prod.tfvars
   allowed_origins = [
     "https://your-actual-domain.com",
     "https://localhost:3000"  # Keep for development
   ]
   ```

3. **Configure OpenSearch Secrets in Key Vault:**
   ```bash
   # After deployment, add these secrets to Key Vault:
   az keyvault secret set --vault-name "your-keyvault" --name "opensearch-endpoint" --value "https://search-bigcommerceinsights-co3k7jb4ukk565b4c3bskzucpm.us-east-1.es.amazonaws.com"
   az keyvault secret set --vault-name "your-keyvault" --name "opensearch-username" --value "admin"
   az keyvault secret set --vault-name "your-keyvault" --name "opensearch-password" --value "VIQInsights@123"
   ```

**Note**: Using external AWS OpenSearch cluster - no Azure Search service created

## 🧪 Testing & Validation

### **Terraform Validation:**
```bash
# Syntax validation
terraform validate

# Plan validation
terraform plan -var-file="terraform.dev.tfvars"

# Security scanning (optional)
terraform plan -var-file="terraform.dev.tfvars" | tee plan.out
```

### **Resource Validation:**
```bash
# Check resource groups
az group list --output table

# Check function apps
az functionapp list --output table

# Check storage accounts
az storage account list --output table
```

## 🎯 Key Benefits

### **Fixed Configuration:**
- ✅ **All variables properly defined** - No missing variable errors
- ✅ **Correct resource configurations** - All Azure resources properly configured
- ✅ **Environment-specific settings** - Development and production optimized separately
- ✅ **Security best practices** - Key Vault integration and secure networking

### **Production-Ready Features:**
- ✅ **High availability** - PostgreSQL zone redundancy in production
- ✅ **Backup retention** - 35 days for production, 7 days for development
- ✅ **Monitoring & alerting** - Comprehensive monitoring setup
- ✅ **Scalability** - Premium Redis and Standard search for production

## 📈 Cost Optimization

### **Development Environment:**
- Estimated cost: **$10-30/month**
- Free tier services where possible
- Azure Table Storage (minimal cost)
- Consumption plan Functions
- No search service costs (using external OpenSearch)

### **Production Environment:**
- Estimated cost: **$50-100/month**
- Premium services for performance
- GRS storage for reliability
- No Redis/PostgreSQL/Search costs

## 🚀 Next Steps

1. **Deploy Development Environment** first for testing
2. **Configure OpenSearch Integration** (see below)
3. **Validate all services** are working correctly
4. **Update security configurations** with real values
5. **Deploy Production Environment** when ready
6. **Monitor costs** and adjust as needed

### **OpenSearch Integration Setup**

After deploying the infrastructure, configure the external AWS OpenSearch cluster:

```bash
# Get your Key Vault name (after deployment)
az keyvault list --query "[].name" -o table

# Add OpenSearch secrets to Key Vault
az keyvault secret set \
  --vault-name "your-keyvault-name" \
  --name "opensearch-endpoint" \
  --value "https://search-bigcommerceinsights-co3k7jb4ukk565b4c3bskzucpm.us-east-1.es.amazonaws.com"

az keyvault secret set \
  --vault-name "your-keyvault-name" \
  --name "opensearch-username" \
  --value "admin"

az keyvault secret set \
  --vault-name "your-keyvault-name" \
  --name "opensearch-password" \
  --value "VIQInsights@123"

# Verify secrets are set
az keyvault secret list --vault-name "your-keyvault-name" -o table
```

---

**⚠️ Important**: This configuration creates real Azure resources that incur costs. Always review the plan before applying and monitor your Azure billing.

**📞 Support**: For issues with this Terraform configuration, check the validation script output and Azure portal for detailed error messages. 