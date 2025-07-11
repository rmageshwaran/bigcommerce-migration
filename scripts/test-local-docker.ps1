# BigCommerce Migration - Local Docker Testing Script
# This script validates that the local Docker environment is working correctly

param(
    [Parameter(Mandatory=$false)]
    [switch]$Detailed,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipOpenSearch
)

Write-Host "🧪 BigCommerce Migration - Local Docker Environment Tests" -ForegroundColor Cyan

# Test results tracking
$testResults = @()

function Test-Endpoint {
    param(
        [string]$Name,
        [string]$Url,
        [int]$ExpectedStatus = 200,
        [int]$TimeoutSec = 10
    )
    
    try {
        $response = Invoke-WebRequest -Uri $Url -TimeoutSec $TimeoutSec -UseBasicParsing
        if ($response.StatusCode -eq $ExpectedStatus) {
            Write-Host "✅ $Name - OK ($($response.StatusCode))" -ForegroundColor Green
            return @{ Name = $Name; Status = "PASS"; Details = "HTTP $($response.StatusCode)" }
        } else {
            Write-Host "❌ $Name - Unexpected status ($($response.StatusCode))" -ForegroundColor Red
            return @{ Name = $Name; Status = "FAIL"; Details = "HTTP $($response.StatusCode)" }
        }
    } catch {
        Write-Host "❌ $Name - Failed ($($_.Exception.Message))" -ForegroundColor Red
        return @{ Name = $Name; Status = "FAIL"; Details = $_.Exception.Message }
    }
}

function Test-OpenSearchEndpoint {
    param(
        [string]$Name,
        [string]$Url,
        [string]$Username,
        [string]$Password
    )
    
    try {
        $auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${Username}:${Password}"))
        $headers = @{ Authorization = "Basic $auth" }
        
        $response = Invoke-WebRequest -Uri $Url -Headers $headers -TimeoutSec 10 -UseBasicParsing
        if ($response.StatusCode -eq 200) {
            Write-Host "✅ $Name - OK ($($response.StatusCode))" -ForegroundColor Green
            return @{ Name = $Name; Status = "PASS"; Details = "HTTP $($response.StatusCode)" }
        } else {
            Write-Host "❌ $Name - Unexpected status ($($response.StatusCode))" -ForegroundColor Red
            return @{ Name = $Name; Status = "FAIL"; Details = "HTTP $($response.StatusCode)" }
        }
    } catch {
        Write-Host "❌ $Name - Failed ($($_.Exception.Message))" -ForegroundColor Red
        return @{ Name = $Name; Status = "FAIL"; Details = $_.Exception.Message }
    }
}

Write-Host ""
Write-Host "🔍 Testing Core Services..." -ForegroundColor Blue

# Test Azurite Storage
Write-Host ""
Write-Host "📦 Testing Azurite Storage Services..." -ForegroundColor Yellow
$testResults += Test-Endpoint "Azurite Blob Service" "http://localhost:10000/devstoreaccount1"
$testResults += Test-Endpoint "Azurite Queue Service" "http://localhost:10001/devstoreaccount1"
$testResults += Test-Endpoint "Azurite Table Service" "http://localhost:10002/devstoreaccount1"

# Test Azure Functions
Write-Host ""
Write-Host "⚡ Testing Azure Functions..." -ForegroundColor Yellow
$testResults += Test-Endpoint "Functions Health Check" "http://localhost:7071/api/health"

# Test Dashboard
Write-Host ""
Write-Host "🎨 Testing React Dashboard..." -ForegroundColor Yellow
$testResults += Test-Endpoint "Dashboard Home Page" "http://localhost:3000"

# Test SignalR (optional)
Write-Host ""
Write-Host "📡 Testing SignalR Service..." -ForegroundColor Yellow
$testResults += Test-Endpoint "SignalR Health" "http://localhost:8080" -ExpectedStatus 404

# Test OpenSearch (external)
if (-not $SkipOpenSearch) {
    Write-Host ""
    Write-Host "🔍 Testing OpenSearch (AWS)..." -ForegroundColor Yellow
    $testResults += Test-OpenSearchEndpoint "OpenSearch Cluster Health" `
        "https://search-bigcommerceinsights-co3k7jb4ukk565b4c3bskzucpm.us-east-1.es.amazonaws.com/_cluster/health" `
        "admin" "VIQInsights@123"
}

# Detailed API Tests
if ($Detailed) {
    Write-Host ""
    Write-Host "🔧 Running Detailed API Tests..." -ForegroundColor Blue
    
    # Test Functions API endpoints
    Write-Host ""
    Write-Host "📋 Testing Functions API Endpoints..." -ForegroundColor Yellow
    
    # Test dashboard API
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:7071/api/dashboard/health" -TimeoutSec 5 -UseBasicParsing
        Write-Host "✅ Dashboard API - OK" -ForegroundColor Green
        $testResults += @{ Name = "Dashboard API"; Status = "PASS"; Details = "HTTP $($response.StatusCode)" }
    } catch {
        Write-Host "⚠️ Dashboard API - May not be implemented yet" -ForegroundColor Yellow
        $testResults += @{ Name = "Dashboard API"; Status = "SKIP"; Details = "Not implemented" }
    }
    
    # Test system health endpoint
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:7071/api/health" -UseBasicParsing
        $healthData = $response.Content | ConvertFrom-Json
        
        if ($healthData.status -eq "healthy") {
            Write-Host "✅ System Health - All services healthy" -ForegroundColor Green
            $testResults += @{ Name = "System Health Check"; Status = "PASS"; Details = "All services healthy" }
        } else {
            Write-Host "⚠️ System Health - Some services degraded" -ForegroundColor Yellow
            $testResults += @{ Name = "System Health Check"; Status = "WARN"; Details = "Some services degraded" }
        }
        
        if ($Detailed -and $healthData.services) {
            Write-Host "   Service Details:" -ForegroundColor Gray
            foreach ($service in $healthData.services.PSObject.Properties) {
                $status = if ($service.Value -eq "healthy") { "✅" } else { "⚠️" }
                Write-Host "     $status $($service.Name): $($service.Value)" -ForegroundColor Gray
            }
        }
    } catch {
        Write-Host "❌ System Health Check - Failed" -ForegroundColor Red
        $testResults += @{ Name = "System Health Check"; Status = "FAIL"; Details = $_.Exception.Message }
    }
}

# Summary
Write-Host ""
Write-Host "📊 Test Results Summary" -ForegroundColor Cyan
Write-Host "========================" -ForegroundColor Cyan

$passCount = ($testResults | Where-Object { $_.Status -eq "PASS" }).Count
$failCount = ($testResults | Where-Object { $_.Status -eq "FAIL" }).Count
$skipCount = ($testResults | Where-Object { $_.Status -eq "SKIP" }).Count
$warnCount = ($testResults | Where-Object { $_.Status -eq "WARN" }).Count
$totalCount = $testResults.Count

Write-Host ""
Write-Host "✅ Passed: $passCount" -ForegroundColor Green
Write-Host "❌ Failed: $failCount" -ForegroundColor Red
Write-Host "⚠️ Warnings: $warnCount" -ForegroundColor Yellow
Write-Host "⏭️ Skipped: $skipCount" -ForegroundColor Gray
Write-Host "📊 Total: $totalCount" -ForegroundColor White

# Show failed tests
if ($failCount -gt 0) {
    Write-Host ""
    Write-Host "❌ Failed Tests:" -ForegroundColor Red
    $testResults | Where-Object { $_.Status -eq "FAIL" } | ForEach-Object {
        Write-Host "   • $($_.Name): $($_.Details)" -ForegroundColor Red
    }
}

# Show warnings
if ($warnCount -gt 0) {
    Write-Host ""
    Write-Host "⚠️ Warnings:" -ForegroundColor Yellow
    $testResults | Where-Object { $_.Status -eq "WARN" } | ForEach-Object {
        Write-Host "   • $($_.Name): $($_.Details)" -ForegroundColor Yellow
    }
}

Write-Host ""
if ($failCount -eq 0) {
    Write-Host "🎉 All critical tests passed! Your Docker environment is ready." -ForegroundColor Green
    
    Write-Host ""
    Write-Host "🚀 Next Steps:" -ForegroundColor Yellow
    Write-Host "   1. Open dashboard: http://localhost:3000" -ForegroundColor White
    Write-Host "   2. Test migration API with BigCommerce credentials" -ForegroundColor White
    Write-Host "   3. Monitor logs: .\scripts\start-local-docker.ps1 -Logs" -ForegroundColor White
    
    exit 0
} else {
    Write-Host "❌ Some tests failed. Check the services and try again." -ForegroundColor Red
    
    Write-Host ""
    Write-Host "🔧 Troubleshooting:" -ForegroundColor Yellow
    Write-Host "   1. Check service logs: .\scripts\start-local-docker.ps1 -Logs" -ForegroundColor White
    Write-Host "   2. Restart services: docker-compose restart" -ForegroundColor White
    Write-Host "   3. Rebuild: .\scripts\start-local-docker.ps1 -Clean -Build" -ForegroundColor White
    
    exit 1
} 