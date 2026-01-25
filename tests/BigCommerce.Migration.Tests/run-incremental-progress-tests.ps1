# PowerShell script to run incremental progress integration tests
# Provides easy commands for testing the complete incremental progress system

param(
    [Parameter(Position=0)]
    [ValidateSet("all", "unit", "integration", "performance", "azure", "endtoend", "quick", "help")]
    [string]$TestType = "help",
    
    [Parameter()]
    [switch]$Verbose,
    
    [Parameter()]
    [switch]$NoCleanup,
    
    [Parameter()]
    [string]$Filter = ""
)

# Color output functions
function Write-Success { param([string]$Message) Write-Host $Message -ForegroundColor Green }
function Write-Warning { param([string]$Message) Write-Host $Message -ForegroundColor Yellow }
function Write-Error { param([string]$Message) Write-Host $Message -ForegroundColor Red }
function Write-Info { param([string]$Message) Write-Host $Message -ForegroundColor Cyan }

function Show-Help {
    Write-Host ""
    Write-Info "=== Incremental Progress Test Runner ==="
    Write-Host ""
    Write-Host "Usage: ./run-incremental-progress-tests.ps1 [TestType] [Options]"
    Write-Host ""
    Write-Host "Test Types:"
    Write-Host "  all          - Run all incremental progress tests (unit + integration + performance)"
    Write-Host "  unit         - Run unit tests only (ProgressTracker, ChunkIncrementEvent, etc.)"
    Write-Host "  integration  - Run integration tests only (end-to-end with real Azure Storage)"
    Write-Host "  performance  - Run performance and load tests"
    Write-Host "  azure        - Run Azure Storage specific tests"
    Write-Host "  endtoend     - Run complete end-to-end integration tests"
    Write-Host "  quick        - Run a quick subset of tests for rapid feedback"
    Write-Host "  help         - Show this help message"
    Write-Host ""
    Write-Host "Options:"
    Write-Host "  -Verbose     - Enable verbose test output"
    Write-Host "  -NoCleanup   - Skip test data cleanup (for debugging)"
    Write-Host "  -Filter      - Filter tests by name pattern"
    Write-Host ""
    Write-Host "Examples:"
    Write-Host "  ./run-incremental-progress-tests.ps1 quick"
    Write-Host "  ./run-incremental-progress-tests.ps1 integration -Verbose"
    Write-Host "  ./run-incremental-progress-tests.ps1 all -Filter 'EndToEnd'"
    Write-Host ""
    Write-Host "Prerequisites:"
    Write-Host "  - Azurite storage emulator running (for integration tests)"
    Write-Host "  - .NET 8 SDK installed"
    Write-Host "  - Test project built (dotnet build)"
    Write-Host ""
}

function Test-Prerequisites {
    Write-Info "Checking prerequisites..."
    
    # Check if dotnet is available
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Error "❌ .NET SDK not found. Please install .NET 8 SDK."
        return $false
    }
    
    # Check if we're in the right directory
    if (-not (Test-Path "BigCommerce.Migration.Tests.csproj")) {
        Write-Error "❌ Not in test project directory. Please run from tests/BigCommerce.Migration.Tests/"
        return $false
    }
    
    # Check if Azurite is running (for integration tests)
    if ($TestType -in @("all", "integration", "azure", "endtoend")) {
        try {
            $azuriteCheck = Test-NetConnection -ComputerName "127.0.0.1" -Port 10002 -WarningAction SilentlyContinue
            if (-not $azuriteCheck.TcpTestSucceeded) {
                Write-Warning "⚠️  Azurite storage emulator not detected. Integration tests may fail."
                Write-Info "To start Azurite: azurite --silent --location ./azurite --debug ./azurite/debug.log"
            } else {
                Write-Success "✅ Azurite storage emulator is running"
            }
        }
        catch {
            Write-Warning "⚠️  Could not check Azurite status. Integration tests may fail."
        }
    }
    
    Write-Success "✅ Prerequisites check completed"
    return $true
}

function Build-Project {
    Write-Info "Building test project..."
    
    $buildResult = dotnet build --configuration Release --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Error "❌ Build failed. Please fix build errors before running tests."
        return $false
    }
    
    Write-Success "✅ Build successful"
    return $true
}

function Run-Tests {
    param(
        [string]$TestFilter,
        [string]$TestName
    )
    
    Write-Info "Running $TestName..."
    Write-Info "Filter: $TestFilter"
    
    $testArgs = @(
        "test",
        "--configuration", "Release",
        "--logger", "console;verbosity=normal"
    )
    
    if ($TestFilter) {
        $testArgs += "--filter"
        $testArgs += $TestFilter
    }
    
    if ($Verbose) {
        $testArgs += "--verbosity"
        $testArgs += "detailed"
    } else {
        $testArgs += "--verbosity"
        $testArgs += "normal"
    }
    
    if ($Filter) {
        if ($TestFilter) {
            $testArgs[-1] = "($($testArgs[-1])) & DisplayName~$Filter"
        } else {
            $testArgs += "--filter"
            $testArgs += "DisplayName~$Filter"
        }
    }
    
    Write-Info "Command: dotnet $($testArgs -join ' ')"
    
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    & dotnet @testArgs
    $stopwatch.Stop()
    
    if ($LASTEXITCODE -eq 0) {
        Write-Success "✅ $TestName completed successfully in $($stopwatch.Elapsed.TotalSeconds.ToString('F1'))s"
        return $true
    } else {
        Write-Error "❌ $TestName failed"
        return $false
    }
}

function Main {
    Write-Info "=== Incremental Progress Test Runner ==="
    Write-Info "Test Type: $TestType"
    Write-Info "Timestamp: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Write-Host ""
    
    if ($TestType -eq "help") {
        Show-Help
        return
    }
    
    # Check prerequisites
    if (-not (Test-Prerequisites)) {
        Write-Error "❌ Prerequisites check failed. Please resolve issues before running tests."
        exit 1
    }
    
    # Build project
    if (-not (Build-Project)) {
        exit 1
    }
    
    Write-Host ""
    
    $overallSuccess = $true
    $testResults = @()
    
    switch ($TestType) {
        "unit" {
            $result = Run-Tests "FullyQualifiedName~IncrementalProgress&Category!=Integration&Category!=Performance" "Unit Tests"
            $testResults += @{ Name = "Unit Tests"; Success = $result }
            $overallSuccess = $overallSuccess -and $result
        }
        
        "integration" {
            $result = Run-Tests "FullyQualifiedName~Integration.IncrementalProgress" "Integration Tests"
            $testResults += @{ Name = "Integration Tests"; Success = $result }
            $overallSuccess = $overallSuccess -and $result
        }
        
        "azure" {
            $result = Run-Tests "FullyQualifiedName~IncrementalProgressAzureStorageTests" "Azure Storage Tests"
            $testResults += @{ Name = "Azure Storage Tests"; Success = $result }
            $overallSuccess = $overallSuccess -and $result
        }
        
        "endtoend" {
            $result = Run-Tests "FullyQualifiedName~IncrementalProgressEndToEndTests" "End-to-End Tests"
            $testResults += @{ Name = "End-to-End Tests"; Success = $result }
            $overallSuccess = $overallSuccess -and $result
        }
        
        "performance" {
            $result = Run-Tests "FullyQualifiedName~Performance.IncrementalProgress" "Performance Tests"
            $testResults += @{ Name = "Performance Tests"; Success = $result }
            $overallSuccess = $overallSuccess -and $result
        }
        
        "quick" {
            # Run a subset of critical tests for quick feedback
            $result1 = Run-Tests "FullyQualifiedName~ProgressTrackerIncrementalTests&TestCategory!=LongRunning" "Quick Unit Tests"
            $result2 = Run-Tests "Method=ProcessEntityChunk_ShouldRecordIncrementalProgress_EndToEnd" "Critical Integration Test"
            
            $testResults += @{ Name = "Quick Unit Tests"; Success = $result1 }
            $testResults += @{ Name = "Critical Integration Test"; Success = $result2 }
            $overallSuccess = $result1 -and $result2
        }
        
        "all" {
            Write-Info "Running complete incremental progress test suite..."
            
            $result1 = Run-Tests "FullyQualifiedName~IncrementalProgress&Category!=Performance" "All Unit & Integration Tests"
            $result2 = Run-Tests "FullyQualifiedName~Performance.IncrementalProgress" "Performance Tests"
            
            $testResults += @{ Name = "Unit & Integration Tests"; Success = $result1 }
            $testResults += @{ Name = "Performance Tests"; Success = $result2 }
            $overallSuccess = $result1 -and $result2
        }
        
        default {
            Write-Error "❌ Unknown test type: $TestType"
            Show-Help
            exit 1
        }
    }
    
    # Summary
    Write-Host ""
    Write-Info "=== Test Results Summary ==="
    
    foreach ($result in $testResults) {
        if ($result.Success) {
            Write-Success "✅ $($result.Name): PASSED"
        } else {
            Write-Error "❌ $($result.Name): FAILED"
        }
    }
    
    Write-Host ""
    
    if ($overallSuccess) {
        Write-Success "🎉 All tests passed successfully!"
        Write-Info "Your incremental progress system is ready for production!"
    } else {
        Write-Error "❌ Some tests failed. Please review the output above."
        exit 1
    }
    
    # Cleanup reminder
    if (-not $NoCleanup -and ($TestType -in @("all", "integration", "azure", "endtoend"))) {
        Write-Host ""
        Write-Info "💡 Test data cleanup is handled automatically by the test framework."
        Write-Info "   If you need to manually cleanup, check Azurite storage explorer."
    }
}

# Run main function
Main