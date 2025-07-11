data "azurerm_client_config" "current" {}

data "azurerm_resource_group" "existing_rg" {
  name = var.resource_group_name
}

data "azurerm_storage_account" "existing_sa" {
  name                = var.storage_account_name
  resource_group_name = var.resource_group_name
}

resource "azurerm_log_analytics_workspace" "workspace" {
  name                = "${var.function_app_name}-workspace"
  location            = data.azurerm_resource_group.existing_rg.location
  resource_group_name = data.azurerm_resource_group.existing_rg.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
}

resource "azurerm_application_insights" "app_insights" {
  name                = "${var.function_app_name}-app-insight"
  location            = data.azurerm_resource_group.existing_rg.location
  resource_group_name = data.azurerm_resource_group.existing_rg.name
  application_type    = "web"
  retention_in_days   = var.ai_retention_in_days
  workspace_id        = azurerm_log_analytics_workspace.workspace.id
}

resource "azurerm_service_plan" "service_plan" {
  name                = "${var.function_app_name}-plan"
  location            = data.azurerm_resource_group.existing_rg.location
  resource_group_name = data.azurerm_resource_group.existing_rg.name
  os_type             = var.os_type
  sku_name            = var.sku_name
}

resource "azurerm_windows_function_app" "function_app" {
  name                       = var.function_app_name
  location                   = data.azurerm_resource_group.existing_rg.location
  resource_group_name        = data.azurerm_resource_group.existing_rg.name
  service_plan_id            = azurerm_service_plan.service_plan.id
  storage_account_name       = data.azurerm_storage_account.existing_sa.name
  storage_account_access_key = data.azurerm_storage_account.existing_sa.primary_access_key

  site_config {
    application_stack {
      dotnet_version = var.dotnet_version
    }
  }

  identity {
    type = "SystemAssigned"
  }

  app_settings = {
    "FUNCTIONS_WORKER_RUNTIME"              = var.worker_runtime
    "WEBSITE_RUN_FROM_PACKAGE"              = var.run_from_package
    "AzureWebJobsStorage"                   = data.azurerm_storage_account.existing_sa.primary_connection_string
    "APPLICATIONINSIGHTS_CONNECTION_STRING" = azurerm_application_insights.app_insights.connection_string
    "QueueName" : "bigcommerce-migration-request-queue",
    "EntityMigrationRequestQueueName" : "bigcommerce-migration-request-queue",
    "OutputQueueName" : "bigcommerce-migration-splitter-queue",
    "AppSettings__BatchSize" : 100,
    "ProductProcessingQueueName" : "product-processing-queue"
  }
}
