# BigCommerce Migration System - Production Infrastructure
# This configuration deploys a complete production-ready migration system

terraform {
  required_version = ">= 1.5"
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
  }
}

# Get current Azure client configuration
data "azurerm_client_config" "current" {}

# Resource Group
resource "azurerm_resource_group" "bigcommerce_migration" {
  name     = var.resource_group_name
  location = var.location

  tags = {
    Environment = var.environment
    Project     = "BigCommerce-Migration"
    ManagedBy   = "Terraform"
  }
}

# Storage Account for Functions and data persistence
resource "azurerm_storage_account" "migration_storage" {
  name                     = "${var.prefix}migrationstorage"
  resource_group_name      = azurerm_resource_group.bigcommerce_migration.name
  location                = azurerm_resource_group.bigcommerce_migration.location
  account_tier             = "Standard"
  account_replication_type = var.environment == "prod" ? "GRS" : "LRS"
  
  blob_properties {
    versioning_enabled = true
    delete_retention_policy {
      days = 30
    }
  }

  tags = local.common_tags
}

# Key Vault for secrets management
resource "azurerm_key_vault" "migration_keyvault" {
  name                = "${var.prefix}-migration-kv"
  location            = azurerm_resource_group.bigcommerce_migration.location
  resource_group_name = azurerm_resource_group.bigcommerce_migration.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  sku_name            = "standard"

  access_policy {
    tenant_id = data.azurerm_client_config.current.tenant_id
    object_id = data.azurerm_client_config.current.object_id

    secret_permissions = [
      "Get", "List", "Set", "Delete", "Backup", "Restore"
    ]
  }

  tags = local.common_tags
}

# Note: Redis and PostgreSQL removed - system uses Azure Table Storage and Azure Search instead
# Redis: In-memory rate limiting sufficient for current scale
# PostgreSQL: Azure Table Storage handles all state management needs

# Note: Azure Search removed - using existing OpenSearch cluster in AWS
# OpenSearch URL and credentials will be provided during E2E testing configuration

# Log Analytics Workspace
resource "azurerm_log_analytics_workspace" "migration_workspace" {
  name                = "${var.prefix}-migration-workspace"
  location           = azurerm_resource_group.bigcommerce_migration.location
  resource_group_name = azurerm_resource_group.bigcommerce_migration.name
  sku                = "PerGB2018"
  retention_in_days  = var.environment == "prod" ? 365 : 30

  tags = local.common_tags
}

# Application Insights for monitoring
resource "azurerm_application_insights" "migration_insights" {
  name                = "${var.prefix}-migration-insights"
  location           = azurerm_resource_group.bigcommerce_migration.location
  resource_group_name = azurerm_resource_group.bigcommerce_migration.name
  application_type   = "web"
  retention_in_days  = var.environment == "prod" ? 365 : 90
  workspace_id       = azurerm_log_analytics_workspace.migration_workspace.id

  tags = local.common_tags
}

# Service Plan for Functions
resource "azurerm_service_plan" "migration_plan" {
  name                = "${var.prefix}-migration-plan"
  location           = azurerm_resource_group.bigcommerce_migration.location
  resource_group_name = azurerm_resource_group.bigcommerce_migration.name
  os_type            = "Linux"
  sku_name           = var.environment == "prod" ? "EP2" : "EP1"

  tags = local.common_tags
}

# Azure Functions App
resource "azurerm_linux_function_app" "migration_functions" {
  name                = "${var.prefix}-migration-functions"
  location           = azurerm_resource_group.bigcommerce_migration.location
  resource_group_name = azurerm_resource_group.bigcommerce_migration.name
  service_plan_id    = azurerm_service_plan.migration_plan.id

  storage_account_name       = azurerm_storage_account.migration_storage.name
  storage_account_access_key = azurerm_storage_account.migration_storage.primary_access_key

  site_config {
    application_stack {
      dotnet_version              = "8.0"
      use_dotnet_isolated_runtime = true
    }
    
    application_insights_connection_string = azurerm_application_insights.migration_insights.connection_string
    application_insights_key               = azurerm_application_insights.migration_insights.instrumentation_key
    
    cors {
      allowed_origins = var.allowed_origins
    }
  }

  identity {
    type = "SystemAssigned"
  }

  app_settings = {
    # Function Runtime
    "FUNCTIONS_WORKER_RUNTIME"              = "dotnet-isolated"
    "WEBSITE_RUN_FROM_PACKAGE"              = "1"
    "FUNCTIONS_EXTENSION_VERSION"           = "~4"
    "ASPNETCORE_ENVIRONMENT"                = var.environment

    # Storage
    "AzureWebJobsStorage"                   = azurerm_storage_account.migration_storage.primary_connection_string

    # Application Insights
    "APPLICATIONINSIGHTS_CONNECTION_STRING" = azurerm_application_insights.migration_insights.connection_string

    # SignalR Connection String (matches ConnectionStrings section in appsettings.json)
    "AzureSignalR"                         = azurerm_signalr_service.migration_signalr.primary_connection_string

    # Queue Configuration (matches appsettings.json)
    "MigrationStartQueueName"              = "migration-start"
    "EntityBatchQueueName"                 = "entity-batch"
    "BatchCompletionQueueName"             = "batch-completion"
    "ProgressUpdateQueueName"              = "progress-update"
    "DeadLetterQueueName"                  = "dead-letter"
    "RetryQueueName"                       = "retry"

    # BigCommerce Configuration
    "BigCommerce__RateLimitRequestsPerSecond" = "12"
    "BigCommerce__UserAgent"                 = "BigCommerce-Migration-System/1.0"

    # OpenSearch Configuration - External AWS OpenSearch cluster (matches OpenSearchConfiguration.cs)
    "OpenSearch__Endpoint"                 = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.migration_keyvault.name};SecretName=opensearch-endpoint)"
    "OpenSearch__Username"                 = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.migration_keyvault.name};SecretName=opensearch-username)"
    "OpenSearch__Password"                 = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.migration_keyvault.name};SecretName=opensearch-password)"
    "OpenSearch__DefaultIndex"             = "migration-${var.environment}"

    # Note: Redis and PostgreSQL configurations removed
    # System uses Azure Table Storage (AzureWebJobsStorage) for all state management
    # Rate limiting uses in-memory coordination (sufficient for current scale)

    # Processing Configuration
    "AppSettings__BatchSize"               = var.environment == "prod" ? "50" : "10"
    "AppSettings__MaxConcurrency"          = var.environment == "prod" ? "10" : "3"
    "AppSettings__RetryAttempts"           = "3"

    # Key Vault Reference
    "KeyVault__VaultUri"                   = azurerm_key_vault.migration_keyvault.vault_uri
  }

  tags = local.common_tags
}

# Grant Functions access to Key Vault
resource "azurerm_key_vault_access_policy" "function_app_policy" {
  key_vault_id = azurerm_key_vault.migration_keyvault.id
  tenant_id    = data.azurerm_client_config.current.tenant_id
  object_id    = azurerm_linux_function_app.migration_functions.identity[0].principal_id

  secret_permissions = [
    "Get", "List"
  ]
}

# Azure Static Web Apps for Dashboard
resource "azurerm_static_site" "migration_dashboard" {
  name                = "${var.prefix}-migration-dashboard"
  resource_group_name = azurerm_resource_group.bigcommerce_migration.name
  location           = "East US2"  # Static Web Apps limited regions
  sku_tier           = var.static_web_app_sku_tier
  sku_size           = var.static_web_app_sku_size

  tags = local.common_tags
}

# SignalR Service for real-time updates
resource "azurerm_signalr_service" "migration_signalr" {
  name                = "${var.prefix}-migration-signalr"
  location           = azurerm_resource_group.bigcommerce_migration.location
  resource_group_name = azurerm_resource_group.bigcommerce_migration.name

  sku {
    name     = var.signalr_sku_name
    capacity = var.signalr_capacity
  }

  service_mode = "Serverless"

  cors {
    allowed_origins = var.allowed_origins
  }

  tags = local.common_tags
}

# Monitoring and Alerting
resource "azurerm_monitor_action_group" "migration_alerts" {
  name                = "${var.prefix}-migration-alerts"
  resource_group_name = azurerm_resource_group.bigcommerce_migration.name
  short_name          = "MigAlert"

  email_receiver {
    name          = "Migration Team"
    email_address = var.alert_email
  }

  tags = local.common_tags
}

# Alert for Function App errors
resource "azurerm_monitor_metric_alert" "function_errors" {
  name                = "${var.prefix}-function-errors"
  resource_group_name = azurerm_resource_group.bigcommerce_migration.name
  scopes              = [azurerm_linux_function_app.migration_functions.id]
  description         = "Alert when function errors exceed threshold"
  severity            = 2
  frequency           = "PT5M"
  window_size         = "PT15M"

  criteria {
    metric_namespace = "Microsoft.Web/sites"
    metric_name      = "Http5xx"
    aggregation      = "Total"
    operator         = "GreaterThan"
    threshold        = 10
  }

  action {
    action_group_id = azurerm_monitor_action_group.migration_alerts.id
  }

  tags = local.common_tags
}

# Note: PostgreSQL password secret removed - PostgreSQL not used
# System uses Azure Table Storage for all data persistence needs

# Local values for tagging
locals {
  common_tags = {
    Environment = var.environment
    Project     = "BigCommerce-Migration"
    ManagedBy   = "Terraform"
    Owner       = "Migration-Team"
  }
}

# Outputs for deployment script
output "function_app_name" {
  description = "Name of the Azure Function App"
  value       = azurerm_linux_function_app.migration_functions.name
}

output "static_web_app_url" {
  description = "URL of the Static Web App"
  value       = azurerm_static_site.migration_dashboard.default_host_name
}

output "signalr_endpoint" {
  description = "SignalR service endpoint"
  value       = azurerm_signalr_service.migration_signalr.hostname
}

output "key_vault_name" {
  description = "Name of the Key Vault"
  value       = azurerm_key_vault.migration_keyvault.name
}

output "storage_account_name" {
  description = "Name of the storage account"
  value       = azurerm_storage_account.migration_storage.name
}

output "resource_group_name" {
  description = "Name of the resource group"
  value       = azurerm_resource_group.bigcommerce_migration.name
} 