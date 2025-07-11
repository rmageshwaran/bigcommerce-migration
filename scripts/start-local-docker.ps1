# BigCommerce Migration - Local Docker Testing Script
# This script starts the complete local testing environment

param(
    [Parameter(Mandatory=$false)]
    [switch]$Build,
    
    [Parameter(Mandatory=$false)]
    [switch]$Clean,
    
    [Parameter(Mandatory=$false)]
    [switch]$Logs,
    
    [Parameter(Mandatory=$false)]
    [switch]$Stop,
    
    [Parameter(Mandatory=$false)]
    [string]$Service = ""
)

Write-Host "🐳 BigCommerce Migration - Local Docker Environment" -ForegroundColor Cyan

# Set working directory to project root
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$projectRoot = Split-Path -Parent $scriptPath
Set-Location $projectRoot

# Function to check Docker is running
function Test-DockerRunning {
    try {
        docker version | Out-Null
        return $true
    } catch {
        return $false
    }
}

# Function to wait for service health
function Wait-ForServiceHealth {
    param($ServiceName, $HealthEndpoint, $MaxWaitMinutes = 5)
    
    Write-Host "⏳ Waiting for $ServiceName to be healthy..." -ForegroundColor Yellow
    $startTime = Get-Date
    $maxWaitTime = $startTime.AddMinutes($MaxWaitMinutes)
    
    do {
        try {
            $response = Invoke-WebRequest -Uri $HealthEndpoint -TimeoutSec 5 -UseBasicParsing
            if ($response.StatusCode -eq 200) {
                Write-Host "✅ $ServiceName is healthy!" -ForegroundColor Green
                return $true
            }
        } catch {
            # Service not ready yet
        }
        
        Start-Sleep -Seconds 10
        $currentTime = Get-Date
        
        if ($currentTime -gt $maxWaitTime) {
            Write-Host "⏰ Timeout waiting for $ServiceName to be healthy" -ForegroundColor Red
            return $false
        }
        
        Write-Host "   Still waiting for $ServiceName..." -ForegroundColor Gray
    } while ($true)
}

# Check if Docker is running
if (-not (Test-DockerRunning)) {
    Write-Host "❌ Docker is not running. Please start Docker Desktop and try again." -ForegroundColor Red
    exit 1
}

# Handle stop command
if ($Stop) {
    Write-Host "🛑 Stopping all services..." -ForegroundColor Yellow
    docker-compose down
    
    if ($Clean) {
        Write-Host "🧹 Cleaning up volumes and images..." -ForegroundColor Yellow
        docker-compose down -v --rmi local
        docker system prune -f
    }
    
    Write-Host "✅ Services stopped successfully!" -ForegroundColor Green
    exit 0
}

# Handle logs command
if ($Logs) {
    if ($Service) {
        Write-Host "📋 Showing logs for $Service..." -ForegroundColor Blue
        docker-compose logs -f $Service
    } else {
        Write-Host "📋 Showing logs for all services..." -ForegroundColor Blue
        docker-compose logs -f
    }
    exit 0
}

# Build images if requested or if they don't exist
if ($Build -or $Clean) {
    Write-Host "🔨 Building Docker images..." -ForegroundColor Blue
    docker-compose build --no-cache
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Docker build failed" -ForegroundColor Red
        exit 1
    }
}

# Start services
Write-Host "🚀 Starting BigCommerce Migration local environment..." -ForegroundColor Green

# Start core services first (Azurite)
Write-Host "📦 Starting Azure Storage Emulator (Azurite)..." -ForegroundColor Blue
docker-compose up -d azurite

# Wait for Azurite to be ready
if (Wait-ForServiceHealth "Azurite" "http://localhost:10000/devstoreaccount1" 2) {
    Write-Host "✅ Azurite is ready!" -ForegroundColor Green
} else {
    Write-Host "❌ Azurite failed to start" -ForegroundColor Red
    docker-compose logs azurite
    exit 1
}

# Start Azure Functions
Write-Host "⚡ Starting Azure Functions..." -ForegroundColor Blue
docker-compose up -d bigcommerce-functions

# Wait for Functions to be ready
if (Wait-ForServiceHealth "Azure Functions" "http://localhost:7071/api/health" 3) {
    Write-Host "✅ Azure Functions is ready!" -ForegroundColor Green
} else {
    Write-Host "❌ Azure Functions failed to start" -ForegroundColor Red
    docker-compose logs bigcommerce-functions
    exit 1
}

# Start Dashboard
Write-Host "🎨 Starting React Dashboard..." -ForegroundColor Blue
docker-compose up -d bigcommerce-dashboard

# Wait for Dashboard to be ready
if (Wait-ForServiceHealth "Dashboard" "http://localhost:3000" 3) {
    Write-Host "✅ Dashboard is ready!" -ForegroundColor Green
} else {
    Write-Host "⚠️ Dashboard may be starting (this can take a few minutes)" -ForegroundColor Yellow
}

# Start SignalR (optional)
Write-Host "📡 Starting SignalR emulator..." -ForegroundColor Blue
docker-compose up -d signalr-emulator

Write-Host ""
Write-Host "🎉 LOCAL ENVIRONMENT READY!" -ForegroundColor Green
Write-Host ""
Write-Host "📋 Service URLs:" -ForegroundColor Cyan
Write-Host "   🌐 Dashboard:      http://localhost:3000" -ForegroundColor White
Write-Host "   ⚡ Azure Functions: http://localhost:7071" -ForegroundColor White
Write-Host "   📦 Azurite Blob:   http://localhost:10000" -ForegroundColor White
Write-Host "   📋 Azurite Queue:  http://localhost:10001" -ForegroundColor White
Write-Host "   📊 Azurite Table:  http://localhost:10002" -ForegroundColor White
Write-Host "   📡 SignalR:        http://localhost:8080" -ForegroundColor White
Write-Host ""
Write-Host "🔧 Useful Commands:" -ForegroundColor Cyan
Write-Host "   View logs:         .\scripts\start-local-docker.ps1 -Logs" -ForegroundColor White
Write-Host "   View specific logs: .\scripts\start-local-docker.ps1 -Logs -Service bigcommerce-functions" -ForegroundColor White
Write-Host "   Stop services:     .\scripts\start-local-docker.ps1 -Stop" -ForegroundColor White
Write-Host "   Rebuild:           .\scripts\start-local-docker.ps1 -Build" -ForegroundColor White
Write-Host "   Clean restart:     .\scripts\start-local-docker.ps1 -Clean -Build" -ForegroundColor White
Write-Host ""
Write-Host "🧪 Test Endpoints:" -ForegroundColor Cyan
Write-Host "   Health Check:      http://localhost:7071/api/health" -ForegroundColor White
Write-Host "   Start Migration:   POST http://localhost:7071/api/migrations/start" -ForegroundColor White
Write-Host "   Dashboard API:     http://localhost:7071/api/dashboard" -ForegroundColor White
Write-Host ""
Write-Host "📊 Next Steps:" -ForegroundColor Yellow
Write-Host "   1. Open http://localhost:3000 to see the dashboard" -ForegroundColor White
Write-Host "   2. Test the health endpoint: curl http://localhost:7071/api/health" -ForegroundColor White
Write-Host "   3. Configure BigCommerce store credentials for testing" -ForegroundColor White
Write-Host "   4. Start a test migration through the dashboard" -ForegroundColor White 