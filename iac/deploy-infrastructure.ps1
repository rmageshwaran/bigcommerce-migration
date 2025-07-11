# BigCommerce Migration - Infrastructure Deployment Script
# This script deploys the infrastructure and configures OpenSearch secrets

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("dev", "prod")]
    [string]$Environment,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipValidation,
    
    [Parameter(Mandatory=$false)]
    [switch]$ConfigureOpenSearchOnly
)

Write-Host "🚀 BigCommerce Migration Infrastructure Deployment" -ForegroundColor Cyan
Write-Host "Environment: $Environment" -ForegroundColor Yellow

# Set the working directory
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $scriptPath

# OpenSearch configuration (matches app configuration)
$openSearchConfig = @{
    Endpoint = "https://search-bigcommerceinsights-co3k7jb4ukk565b4c3bskzucpm.us-east-1.es.amazonaws.com"
    Username = "admin"
    Password = "VIQInsights@123"
}

if (-not $ConfigureOpenSearchOnly) {
    # Step 1: Validate Terraform
    if (-not $SkipValidation) {
        Write-Host "🔍 Step 1: Validating Terraform configuration..." -ForegroundColor Blue
        .\validate-terraform.ps1 -Environment $Environment
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ Terraform validation failed. Aborting deployment." -ForegroundColor Red
            exit 1
        }
    }

    # Step 2: Deploy Infrastructure
    Write-Host "🏗️ Step 2: Deploying Azure infrastructure..." -ForegroundColor Blue
    
    # Initialize Terraform
    terraform init -backend-config="config_$Environment.azurerm.tfbackend"
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Terraform initialization failed" -ForegroundColor Red
        exit 1
    }
    
    # Apply Terraform
    Write-Host "📋 Creating deployment plan..." -ForegroundColor Blue
    terraform plan -var-file="terraform.$Environment.tfvars" -out="terraform.$Environment.tfplan"
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Terraform plan failed" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "🚀 Deploying infrastructure..." -ForegroundColor Green
    terraform apply "terraform.$Environment.tfplan"
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Terraform deployment failed" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "✅ Infrastructure deployment completed successfully!" -ForegroundColor Green
}

# Step 3: Configure OpenSearch
Write-Host "🔧 Step 3: Configuring OpenSearch integration..." -ForegroundColor Blue

# Get the Key Vault name from Terraform output
try {
    $keyVaultName = terraform output -raw key_vault_name
} catch {
    # Fallback: try to find Key Vault by naming convention
    $prefix = if ($Environment -eq "prod") { "vortexiq" } else { "vortexiqdev" }
    $keyVaultName = "$prefix-migration-kv"
    Write-Host "⚠️ Using fallback Key Vault name: $keyVaultName" -ForegroundColor Yellow
}

Write-Host "🔑 Configuring Key Vault: $keyVaultName" -ForegroundColor Blue

# Configure OpenSearch secrets (correct property names)
Write-Host "📝 Adding OpenSearch endpoint..." -ForegroundColor Gray
az keyvault secret set `
    --vault-name $keyVaultName `
    --name "opensearch-endpoint" `
    --value $openSearchConfig.Endpoint `
    --output none

Write-Host "📝 Adding OpenSearch username..." -ForegroundColor Gray
az keyvault secret set `
    --vault-name $keyVaultName `
    --name "opensearch-username" `
    --value $openSearchConfig.Username `
    --output none

Write-Host "📝 Adding OpenSearch password..." -ForegroundColor Gray
az keyvault secret set `
    --vault-name $keyVaultName `
    --name "opensearch-password" `
    --value $openSearchConfig.Password `
    --output none

# Verify secrets
Write-Host "🔍 Verifying secrets configuration..." -ForegroundColor Blue
$secrets = az keyvault secret list --vault-name $keyVaultName --query "[?starts_with(name, 'opensearch')].name" -o tsv

if ($secrets -and $secrets.Count -ge 3) {
    Write-Host "✅ OpenSearch secrets configured successfully:" -ForegroundColor Green
    foreach ($secret in $secrets) {
        Write-Host "   ✓ $secret" -ForegroundColor White
    }
} else {
    Write-Host "⚠️ Warning: Expected 3 OpenSearch secrets, found $($secrets.Count)" -ForegroundColor Yellow
}

# Step 4: Get deployment information
Write-Host "📊 Step 4: Deployment Summary" -ForegroundColor Blue

try {
    $functionAppName = terraform output -raw function_app_name
    $staticWebAppUrl = terraform output -raw static_web_app_url
    $signalrEndpoint = terraform output -raw signalr_endpoint
    
    Write-Host ""
    Write-Host "🎉 DEPLOYMENT SUCCESSFUL!" -ForegroundColor Green
    Write-Host ""
    Write-Host "📋 Deployment Details:" -ForegroundColor Cyan
    Write-Host "   Environment: $Environment" -ForegroundColor White
    Write-Host "   Function App: $functionAppName" -ForegroundColor White
    Write-Host "   Dashboard URL: $staticWebAppUrl" -ForegroundColor White
    Write-Host "   SignalR Endpoint: $signalrEndpoint" -ForegroundColor White
    Write-Host "   Key Vault: $keyVaultName" -ForegroundColor White
    Write-Host ""
    Write-Host "🔧 OpenSearch Configuration:" -ForegroundColor Cyan
    Write-Host "   Endpoint: $($openSearchConfig.Endpoint)" -ForegroundColor White
    Write-Host "   Username: $($openSearchConfig.Username)" -ForegroundColor White
    Write-Host "   ✅ Credentials stored securely in Key Vault" -ForegroundColor Green
    
} catch {
    Write-Host "⚠️ Could not retrieve all deployment outputs. Check Terraform outputs manually:" -ForegroundColor Yellow
    Write-Host "   terraform output" -ForegroundColor Gray
}

Write-Host ""
Write-Host "🚀 NEXT STEPS:" -ForegroundColor Yellow
Write-Host "   1. Deploy your Function App code to: $functionAppName" -ForegroundColor White
Write-Host "   2. Deploy your dashboard to the Static Web App" -ForegroundColor White
Write-Host "   3. Test the migration system with E2E tests" -ForegroundColor White
Write-Host "   4. Monitor costs in Azure Portal" -ForegroundColor White

Write-Host ""
Write-Host "✅ Infrastructure deployment and OpenSearch configuration completed!" -ForegroundColor Green 