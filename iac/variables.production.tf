variable "resource_group_name" {
  description = "Name of the resource group for BigCommerce migration resources"
  type        = string
  default     = "bigcommerce-migration-rg"
}

variable "location" {
  description = "Azure region for resource deployment"
  type        = string
  default     = "East US"
}

variable "environment" {
  description = "Environment name (dev, staging, prod)"
  type        = string
  default     = "dev"
  
  validation {
    condition     = contains(["dev", "staging", "prod"], var.environment)
    error_message = "Environment must be dev, staging, or prod."
  }
}

variable "prefix" {
  description = "Prefix for all resource names"
  type        = string
  default     = "bcmigration"
  
  validation {
    condition     = length(var.prefix) <= 10
    error_message = "Prefix must be 10 characters or less."
  }
}

variable "postgres_admin_password" {
  description = "Administrator password for PostgreSQL server"
  type        = string
  sensitive   = true
}

variable "allowed_origins" {
  description = "List of allowed origins for CORS"
  type        = list(string)
  default     = ["https://localhost:3000"]
}

variable "alert_email" {
  description = "Email address for monitoring alerts"
  type        = string
}

# Environment-specific scaling variables
variable "function_sku" {
  description = "SKU for Azure Functions service plan"
  type        = map(string)
  default = {
    dev     = "EP1"
    staging = "EP1"
    prod    = "EP2"
  }
}

variable "redis_capacity" {
  description = "Redis cache capacity by environment"
  type        = map(number)
  default = {
    dev     = 1
    staging = 1
    prod    = 2
  }
}

variable "postgres_storage_mb" {
  description = "PostgreSQL storage in MB by environment"
  type        = map(number)
  default = {
    dev     = 8192
    staging = 16384
    prod    = 32768
  }
}

variable "postgres_sku" {
  description = "PostgreSQL SKU by environment"
  type        = map(string)
  default = {
    dev     = "GP_Standard_D2s_v3"
    staging = "GP_Standard_D2s_v3"
    prod    = "GP_Standard_D4s_v3"
  }
}

variable "log_retention_days" {
  description = "Log retention in days by environment"
  type        = map(number)
  default = {
    dev     = 7
    staging = 30
    prod    = 365
  }
}

variable "backup_retention_days" {
  description = "Backup retention in days by environment"
  type        = map(number)
  default = {
    dev     = 7
    staging = 14
    prod    = 35
  }
}

variable "search_sku" {
  description = "Azure Search SKU by environment"
  type        = map(string)
  default = {
    dev     = "basic"
    staging = "basic"
    prod    = "standard"
  }
}

variable "signalr_sku" {
  description = "SignalR SKU by environment"
  type        = map(string)
  default = {
    dev     = "Free_F1"
    staging = "Free_F1"
    prod    = "Standard_S1"
  }
}

variable "static_web_app_sku" {
  description = "Static Web App SKU by environment"
  type        = map(string)
  default = {
    dev     = "Free"
    staging = "Free"
    prod    = "Standard"
  }
}

# BigCommerce-specific variables
variable "bigcommerce_rate_limit" {
  description = "BigCommerce API rate limit requests per second"
  type        = number
  default     = 12
}

variable "batch_size" {
  description = "Default batch size for entity processing by environment"
  type        = map(number)
  default = {
    dev     = 10
    staging = 25
    prod    = 50
  }
}

variable "max_concurrency" {
  description = "Maximum concurrency for parallel processing by environment"
  type        = map(number)
  default = {
    dev     = 3
    staging = 5
    prod    = 10
  }
}

variable "retry_attempts" {
  description = "Number of retry attempts for failed operations"
  type        = number
  default     = 3
}

# Tags
variable "common_tags" {
  description = "Common tags to apply to all resources"
  type        = map(string)
  default = {
    Project   = "BigCommerce-Migration"
    ManagedBy = "Terraform"
  }
} 