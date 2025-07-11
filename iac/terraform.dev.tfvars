# Core Infrastructure Configuration
resource_group_name     = "VortexIQ-Dev"
location               = "East US"
environment            = "dev"
prefix                 = "vortexiqdev"

# Storage Configuration
storage_account_name    = "vortexiqdevsa"

# Function App Configuration
function_app_name       = "vortexiq-migration-functions-dev"
os_type                = "Windows"
sku_name               = "Y1"
dotnet_version         = "v8.0"
worker_runtime         = "dotnet-isolated"
run_from_package       = "1"

# Application Insights Configuration
ai_retention_in_days    = 30

# Development Security - IMPORTANT: Set these values securely
alert_email            = "dev-alerts@yourcompany.com"
allowed_origins        = [
  "https://localhost:3000",
  "https://localhost:5173"
]

# Note: Redis and PostgreSQL configurations removed
# System uses Azure Table Storage (included in Storage Account) for all data persistence

# Note: Azure Search configuration removed - using external OpenSearch cluster in AWS

# SignalR Configuration (Development)
signalr_sku_name = "Free_F1"
signalr_capacity = 1

# Static Web App Configuration (Development)
static_web_app_sku_tier = "Free"
static_web_app_sku_size = "Free"
