# BigCommerce Migration - Terraform Validation Script
# This script validates the Terraform configuration for both dev and prod environments

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("dev", "prod")]
    [string]$Environment = "dev"
)

Write-Host "🔍 Validating Terraform Configuration for $Environment environment..." -ForegroundColor Yellow

# Set the working directory
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $scriptPath

# Check if Terraform is installed
Write-Host "📋 Checking Terraform installation..." -ForegroundColor Blue
if (!(Get-Command "terraform" -ErrorAction SilentlyContinue)) {
    Write-Host "❌ Terraform is not installed or not in PATH" -ForegroundColor Red
    exit 1
}

$terraformVersion = terraform version
Write-Host "✅ Terraform found: $($terraformVersion.Split("`n")[0])" -ForegroundColor Green

# Initialize Terraform
Write-Host "🔧 Initializing Terraform..." -ForegroundColor Blue
terraform init -backend-config="config_$Environment.azurerm.tfbackend"

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Terraform initialization failed" -ForegroundColor Red
    exit 1
}

# Validate configuration
Write-Host "🔍 Validating Terraform configuration..." -ForegroundColor Blue
terraform validate

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Terraform validation failed" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Terraform configuration is valid!" -ForegroundColor Green

# Plan with environment-specific variables
Write-Host "📋 Creating Terraform plan for $Environment..." -ForegroundColor Blue
terraform plan -var-file="terraform.$Environment.tfvars" -input=false

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Terraform plan failed" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Terraform plan completed successfully!" -ForegroundColor Green
Write-Host "🎉 All validation checks passed for $Environment environment!" -ForegroundColor Cyan

# Security reminders
Write-Host ""
Write-Host "🔐 SECURITY REMINDERS:" -ForegroundColor Yellow
Write-Host "   1. Update alert_email with your actual email address" -ForegroundColor White
Write-Host "   2. Review allowed_origins for your domain" -ForegroundColor White
Write-Host "   3. Consider using Azure Key Vault for sensitive values" -ForegroundColor White
Write-Host ""
Write-Host "📊 ARCHITECTURE NOTES:" -ForegroundColor Green
Write-Host "   ✅ System uses Azure Table Storage for all data persistence" -ForegroundColor White
Write-Host "   ✅ No Redis or PostgreSQL needed (simplified architecture)" -ForegroundColor White
Write-Host "   ✅ In-memory rate limiting sufficient for current scale" -ForegroundColor White
Write-Host "   ✅ Uses external AWS OpenSearch cluster (no Azure Search)" -ForegroundColor White
Write-Host ""
Write-Host "🔧 POST-DEPLOYMENT:" -ForegroundColor Yellow
Write-Host "   📝 Add OpenSearch credentials to Key Vault" -ForegroundColor White
Write-Host "   📝 Configure opensearch-endpoint, opensearch-username, opensearch-password secrets" -ForegroundColor White 