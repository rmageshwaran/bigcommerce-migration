# Core Infrastructure Configuration
resource_group_name     = "VortexIQ"
location               = "East US"
environment            = "prod"
prefix                 = "vortexiq"

# Storage Configuration
storage_account_name    = "vortexiqprodsa"

# Function App Configuration
function_app_name       = "vortexiq-migration-functions"
os_type                = "Windows"
sku_name               = "Y1"
dotnet_version         = "v8.0"
worker_runtime         = "dotnet-isolated"
run_from_package       = "1"

# Application Insights Configuration
ai_retention_in_days    = 365

# Production Security - IMPORTANT: Set these values securely
alert_email            = "alerts@yourcompany.com"
allowed_origins        = [
  "https://vortexiq-migration-dashboard.azurestaticapps.net",
  "https://localhost:3000"
]

# Note: Redis and PostgreSQL configurations removed
# System uses Azure Table Storage (included in Storage Account) for all data persistence

# Note: Azure Search configuration removed - using external OpenSearch cluster in AWS

# SignalR Configuration (Production)
signalr_sku_name = "Standard_S1"
signalr_capacity = 2

# Static Web App Configuration (Production)
static_web_app_sku_tier = "Standard"
static_web_app_sku_size = "Standard"
