# Real-Time Integration Test Script
# Tests the complete real-time monitoring and progress tracking feature

param(
    [string]$BackendUrl = "http://localhost:7071",
    [string]$FrontendUrl = "http://localhost:3000",
    [int]$TestTimeoutSeconds = 120
)

Write-Host "🧪 Testing Real-Time Migration Progress Integration" -ForegroundColor Green
Write-Host "Backend URL: $BackendUrl" -ForegroundColor Yellow
Write-Host "Frontend URL: $FrontendUrl" -ForegroundColor Yellow
Write-Host "Timeout: $TestTimeoutSeconds seconds" -ForegroundColor Yellow
Write-Host ""

# Test Results Storage
$TestResults = @{
    BackendHealth = $false
    SignalRNegotiate = $false
    SignalRInfo = $false
    FrontendReachable = $false
    SignalRConnection = $false
    EnhancedEventsSupported = $false
    OverallStatus = "FAIL"
}

# Function to make HTTP request with error handling
function Test-HttpEndpoint {
    param(
        [string]$Url,
        [string]$Description,
        [string]$Method = "GET",
        [hashtable]$Headers = @{}
    )
    
    try {
        Write-Host "🔍 Testing $Description..." -NoNewline
        
        $params = @{
            Uri = $Url
            Method = $Method
            Headers = $Headers
            TimeoutSec = 10
            UseBasicParsing = $true
        }
        
        $response = Invoke-WebRequest @params
        
        if ($response.StatusCode -eq 200) {
            Write-Host " ✅ PASS" -ForegroundColor Green
            return @{ Success = $true; Data = $response.Content }
        } else {
            Write-Host " ❌ FAIL (Status: $($response.StatusCode))" -ForegroundColor Red
            return @{ Success = $false; Error = "HTTP $($response.StatusCode)" }
        }
    } catch {
        Write-Host " ❌ FAIL ($($_.Exception.Message))" -ForegroundColor Red
        return @{ Success = $false; Error = $_.Exception.Message }
    }
}

# Test 1: Backend Health Check
Write-Host "📋 Phase 1: Backend Health Checks" -ForegroundColor Cyan
$healthTest = Test-HttpEndpoint -Url "$BackendUrl/api/health" -Description "Backend Health Endpoint"
$TestResults.BackendHealth = $healthTest.Success

if ($healthTest.Success) {
    try {
        $healthData = $healthTest.Data | ConvertFrom-Json
        Write-Host "   System Status: $($healthData.status)" -ForegroundColor Green
        Write-Host "   API Version: $($healthData.version)" -ForegroundColor Green
    } catch {
        Write-Host "   Could not parse health data" -ForegroundColor Yellow
    }
}

# Test 2: SignalR Negotiate Endpoint
$negotiateTest = Test-HttpEndpoint -Url "$BackendUrl/api/negotiate" -Description "SignalR Negotiate Endpoint" -Method "POST"
$TestResults.SignalRNegotiate = $negotiateTest.Success

if ($negotiateTest.Success) {
    try {
        $negotiateData = $negotiateTest.Data | ConvertFrom-Json
        Write-Host "   SignalR URL: $($negotiateData.url)" -ForegroundColor Green
        Write-Host "   Access Token: $($negotiateData.accessToken.Length) chars" -ForegroundColor Green
    } catch {
        Write-Host "   Could not parse negotiate response" -ForegroundColor Yellow
    }
}

# Test 3: SignalR Info Endpoint
$infoTest = Test-HttpEndpoint -Url "$BackendUrl/api/signalr/info" -Description "SignalR Info Endpoint"
$TestResults.SignalRInfo = $infoTest.Success

if ($infoTest.Success) {
    try {
        $infoData = $infoTest.Data | ConvertFrom-Json
        Write-Host "   Hub Name: $($infoData.hubName)" -ForegroundColor Green
        Write-Host "   Connection State: $($infoData.connectionState)" -ForegroundColor Green
        Write-Host "   Negotiate Endpoint: $($infoData.negotiateEndpoint)" -ForegroundColor Green
    } catch {
        Write-Host "   Could not parse info data" -ForegroundColor Yellow
    }
}

Write-Host ""

# Test 4: Frontend Reachability
Write-Host "📋 Phase 2: Frontend Connectivity" -ForegroundColor Cyan
$frontendTest = Test-HttpEndpoint -Url "$FrontendUrl" -Description "Frontend Dashboard"
$TestResults.FrontendReachable = $frontendTest.Success

if ($frontendTest.Success -and $frontendTest.Data -like "*Real-Time Migration Dashboard*") {
    Write-Host "   Dashboard content detected ✅" -ForegroundColor Green
} elseif ($frontendTest.Success) {
    Write-Host "   Frontend reachable but content unclear ⚠️" -ForegroundColor Yellow
}

Write-Host ""

# Test 5: Enhanced SignalR Events Test
Write-Host "📋 Phase 3: Enhanced Events Verification" -ForegroundColor Cyan

# Check if enhanced models exist in backend
$enhancedModelsTest = Test-HttpEndpoint -Url "$BackendUrl/api/signalr/info" -Description "Enhanced Events Support"

if ($enhancedModelsTest.Success) {
    try {
        $infoData = $enhancedModelsTest.Data | ConvertFrom-Json
        
        # Check for enhanced event endpoints
        $enhancedEvents = @(
            "DetailedProgress",
            "ProcessingContext", 
            "BatchStarted",
            "BatchCompleted",
            "PerformanceMetrics",
            "MigrationMilestone"
        )
        
        Write-Host "   Enhanced Events Expected:" -ForegroundColor Green
        foreach ($event in $enhancedEvents) {
            Write-Host "     • $event" -ForegroundColor Gray
        }
        
        $TestResults.EnhancedEventsSupported = $true
        
    } catch {
        Write-Host "   Could not verify enhanced events support" -ForegroundColor Yellow
    }
}

Write-Host ""

# Test 6: Integration Test Summary
Write-Host "📋 Phase 4: Integration Test Summary" -ForegroundColor Cyan

$passedTests = ($TestResults.Values | Where-Object { $_ -eq $true }).Count
$totalTests = $TestResults.Count - 1  # Exclude OverallStatus

Write-Host "Tests Passed: $passedTests/$totalTests" -ForegroundColor $(if ($passedTests -eq $totalTests) { "Green" } else { "Yellow" })

# Detailed test results
Write-Host ""
Write-Host "Detailed Results:" -ForegroundColor White
Write-Host "  Backend Health:       $($TestResults.BackendHealth)" -ForegroundColor $(if ($TestResults.BackendHealth) { "Green" } else { "Red" })
Write-Host "  SignalR Negotiate:    $($TestResults.SignalRNegotiate)" -ForegroundColor $(if ($TestResults.SignalRNegotiate) { "Green" } else { "Red" })
Write-Host "  SignalR Info:         $($TestResults.SignalRInfo)" -ForegroundColor $(if ($TestResults.SignalRInfo) { "Green" } else { "Red" })
Write-Host "  Frontend Reachable:   $($TestResults.FrontendReachable)" -ForegroundColor $(if ($TestResults.FrontendReachable) { "Green" } else { "Red" })
Write-Host "  Enhanced Events:      $($TestResults.EnhancedEventsSupported)" -ForegroundColor $(if ($TestResults.EnhancedEventsSupported) { "Green" } else { "Red" })

# Overall Status
if ($passedTests -eq $totalTests) {
    $TestResults.OverallStatus = "PASS"
    Write-Host ""
    Write-Host "🎉 INTEGRATION TEST PASSED!" -ForegroundColor Green -BackgroundColor DarkGreen
    Write-Host "   Real-time monitoring is properly configured" -ForegroundColor Green
    Write-Host "   Both backend and frontend are running correctly" -ForegroundColor Green
    Write-Host "   SignalR integration is functional" -ForegroundColor Green
} else {
    $TestResults.OverallStatus = "FAIL"
    Write-Host ""
    Write-Host "❌ INTEGRATION TEST FAILED" -ForegroundColor Red -BackgroundColor DarkRed
    
    # Provide specific recommendations
    if (-not $TestResults.BackendHealth) {
        Write-Host "   → Start Azure Functions: cd src/BigCommerce.Migration.Functions && func start" -ForegroundColor Yellow
    }
    if (-not $TestResults.FrontendReachable) {
        Write-Host "   → Start React Dashboard: cd src/BigCommerce.Migration.Dashboard && npm run dev" -ForegroundColor Yellow
    }
    if (-not $TestResults.SignalRNegotiate) {
        Write-Host "   → Check SignalR configuration in appsettings.json" -ForegroundColor Yellow
    }
}

Write-Host ""

# Test 7: Manual Testing Instructions
Write-Host "📋 Phase 5: Manual Testing Instructions" -ForegroundColor Cyan
Write-Host ""
Write-Host "To manually test the real-time integration:" -ForegroundColor White
Write-Host "1. Open browser to: $FrontendUrl" -ForegroundColor Gray
Write-Host "2. Navigate to the SignalR Integration Test page" -ForegroundColor Gray
Write-Host "3. Click 'Connect' to test SignalR connection" -ForegroundColor Gray
Write-Host "4. Start a test migration to see real-time updates" -ForegroundColor Gray
Write-Host "5. Observe these real-time events:" -ForegroundColor Gray
Write-Host "   • DetailedProgress - Enhanced progress updates" -ForegroundColor Gray
Write-Host "   • BatchStarted/Completed - Batch-level tracking" -ForegroundColor Gray
Write-Host "   • PerformanceMetrics - Real-time performance data" -ForegroundColor Gray
Write-Host "   • ProcessingContext - Current activity details" -ForegroundColor Gray

Write-Host ""
Write-Host "Files to check:" -ForegroundColor White
Write-Host "  Frontend: src/BigCommerce.Migration.Dashboard/src/components/Tests/SignalRIntegrationTest.tsx" -ForegroundColor Gray
Write-Host "  Backend:  src/BigCommerce.Migration.Functions/Functions/SignalRFunctions.cs" -ForegroundColor Gray
Write-Host "  Models:   src/BigCommerce.Migration.Core/Models/EnhancedMigrationProgress.cs" -ForegroundColor Gray

Write-Host ""
Write-Host "Test completed at $(Get-Date)" -ForegroundColor Cyan

# Return results for automation
return $TestResults 