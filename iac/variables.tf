# Core Infrastructure Variables
variable "resource_group_name" {
  description = "The name of the resource group"
  type        = string
}

variable "location" {
  description = "The Azure region where resources will be deployed"
  type        = string
  default     = "East US"
}

variable "environment" {
  description = "The deployment environment (dev, staging, prod)"
  type        = string
  default     = "prod"
}

variable "prefix" {
  description = "A prefix for naming resources"
  type        = string
  default     = "bigcommerce"
}

# Storage Configuration
variable "storage_account_name" {
  description = "The name of the storage account"
  type        = string
}

# Function App Configuration
variable "function_app_name" {
  description = "Name of the Azure Function App"
  type        = string
}

variable "os_type" {
  description = "Operating system type (Linux or Windows)"
  type        = string
  default     = "Windows"
}

variable "sku_name" {
  description = "Pricing tier (e.g., Y1 for Consumption plan)"
  type        = string
  default     = "Y1"
}

variable "dotnet_version" {
  description = "Version of .NET for the Function App"
  type        = string
  default     = "v8.0"
}

variable "worker_runtime" {
  description = "Runtime for the function app"
  type        = string
  default     = "dotnet"
}

variable "run_from_package" {
  description = "Run the function app from a package"
  type        = string
  default     = "1"
}

# Application Insights Configuration
variable "ai_retention_in_days" {
  description = "Retention period for Application Insights logs in days"
  type        = number
  default     = 90
}

# Production-specific Variables
variable "allowed_origins" {
  description = "List of allowed origins for CORS"
  type        = list(string)
  default     = ["https://localhost:3000"]
}

variable "alert_email" {
  description = "Email address for alerts and notifications"
  type        = string
}

# Note: Redis and PostgreSQL variables removed
# System uses Azure Table Storage (included in Storage Account) for all data needs

# Note: Azure Search variables removed - using external OpenSearch cluster in AWS

# SignalR Configuration
variable "signalr_sku_name" {
  description = "The SKU name for SignalR service"
  type        = string
  default     = "Free_F1"
}

variable "signalr_capacity" {
  description = "The capacity for SignalR service"
  type        = number
  default     = 1
}

# Static Web App Configuration
variable "static_web_app_sku_tier" {
  description = "The SKU tier for Static Web App"
  type        = string
  default     = "Free"
}

variable "static_web_app_sku_size" {
  description = "The SKU size for Static Web App"
  type        = string
  default     = "Free"
}
