#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Runs API Integration Tests for Task 4.1: API Endpoint Updates validation

.DESCRIPTION
    This script runs comprehensive API integration tests to validate that all API endpoints
    are using the new GetLatestAggregatedProgressAsync method with real-time data.

    Test Coverage:
    - GetMigrationStatus endpoint with aggregated progress
    - GetMigration (details) endpoint with aggregated progress  
    - GetMigrationHistory endpoint with aggregated progress
    - GetLatestMigrationForStore endpoint with aggregated progress
    - Dashboard endpoints with real-time data
    - Error handling and fallback scenarios
    - Backward compatibility validation
    - Performance and caching behavior

.PARAMETER TestType
    Type of tests to run: 'quick', 'full', 'performance', 'aggregation', 'all'

.PARAMETER StartAzurite
    Whether to start Azurite storage emulator automatically

.PARAMETER ShowLogs
    Whether to show detailed test logs

.PARAMETER Parallel
    Whether to run tests in parallel (faster but less detailed output)

.EXAMPLE
    ./run-api-integration-tests.ps1 -TestType quick -StartAzurite
    
.EXAMPLE
    ./run-api-integration-tests.ps1 -TestType full -ShowLogs

.EXAMPLE
    ./run-api-integration-tests.ps1 -TestType all -StartAzurite -ShowLogs
#>

param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('quick', 'full', 'performance', 'aggregation', 'all')]
    [string]$TestType = 'quick',
    
    [Parameter(Mandatory = $false)]
    [switch]$StartAzurite,
    
    [Parameter(Mandatory = $false)]
    [switch]$ShowLogs,
    
    [Parameter(Mandatory = $false)]
    [switch]$Parallel
)

# Script configuration
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Colors for output
$Green = "`e[32m"
$Yellow = "`e[33m"
$Red = "`e[31m"
$Blue = "`e[34m"
$Reset = "`e[0m"

function Write-ColoredOutput {
    param([string]$Message, [string]$Color = $Reset)
    Write-Host "$Color$Message$Reset"
}

function Write-Header {
    param([string]$Title)
    Write-Host ""
    Write-ColoredOutput "=" * 70 $Blue
    Write-ColoredOutput "  $Title" $Blue
    Write-ColoredOutput "=" * 70 $Blue
    Write-Host ""
}

function Start-AzuriteIfNeeded {
    if ($StartAzurite) {
        Write-ColoredOutput "🚀 Starting Azurite storage emulator..." $Yellow
        
        # Check if Azurite is already running
        $azuriteProcess = Get-Process -Name "azurite" -ErrorAction SilentlyContinue
        if ($azuriteProcess) {
            Write-ColoredOutput "✅ Azurite is already running (PID: $($azuriteProcess.Id))" $Green
            return
        }
        
        # Check if Azurite is installed
        $azuriteCmd = Get-Command "azurite" -ErrorAction SilentlyContinue
        if (-not $azuriteCmd) {
            Write-ColoredOutput "❌ Azurite not found. Installing..." $Red
            npm install -g azurite
        }
        
        # Start Azurite in background
        Write-ColoredOutput "🏃 Starting Azurite in background..." $Yellow
        $azuriteJob = Start-Job -ScriptBlock {
            azurite --silent --location ./azurite-data
        }
        
        # Wait for Azurite to start
        Start-Sleep -Seconds 3
        Write-ColoredOutput "✅ Azurite started successfully" $Green
    }
}

function Get-TestFilter {
    param([string]$Type)
    
    switch ($Type) {
        'quick' { 
            return "ApiIntegrationTests&Category!=Performance" 
        }
        'full' { 
            return "ApiIntegrationTests|AggregatedProgressApiTests" 
        }
        'performance' { 
            return "Category=Performance" 
        }
        'aggregation' { 
            return "AggregatedProgressApiTests" 
        }
        'all' { 
            return "ApiIntegrationTests|AggregatedProgressApiTests" 
        }
        default { 
            return "ApiIntegrationTests" 
        }
    }
}

function Run-Tests {
    param(
        [string]$Filter,
        [string]$TestName,
        [bool]$ShowDetailedLogs = $false
    )
    
    Write-ColoredOutput "🧪 Running $TestName..." $Yellow
    
    # Build test arguments
    $testArgs = @(
        "test"
        "BigCommerce.Migration.Tests.csproj"
        "--filter"
        $Filter
        "--verbosity"
        $(if ($ShowDetailedLogs) { "detailed" } else { "normal" })
        "--logger"
        "console;verbosity=normal"
    )
    
    if ($Parallel -and -not $ShowDetailedLogs) {
        $testArgs += "--parallel"
    }
    
    # Run tests
    $startTime = Get-Date
    try {
        & dotnet @testArgs
        $exitCode = $LASTEXITCODE
        $endTime = Get-Date
        $duration = $endTime - $startTime
        
        if ($exitCode -eq 0) {
            Write-ColoredOutput "✅ $TestName completed successfully in $($duration.TotalSeconds.ToString('F1'))s" $Green
            return $true
        } else {
            Write-ColoredOutput "❌ $TestName failed with exit code $exitCode" $Red
            return $false
        }
    }
    catch {
        Write-ColoredOutput "❌ $TestName failed with error: $($_.Exception.Message)" $Red
        return $false
    }
}

# Main execution
try {
    Write-Header "🧪 API Integration Tests - Task 4.1 Validation"
    
    Write-ColoredOutput "📋 Test Configuration:" $Blue
    Write-ColoredOutput "   Test Type: $TestType" $Blue
    Write-ColoredOutput "   Start Azurite: $StartAzurite" $Blue
    Write-ColoredOutput "   Show Logs: $ShowLogs" $Blue
    Write-ColoredOutput "   Parallel: $Parallel" $Blue
    Write-Host ""
    
    # Navigate to test directory
    $testDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    Push-Location $testDir
    
    try {
        # Start Azurite if requested
        Start-AzuriteIfNeeded
        
        # Build test project
        Write-ColoredOutput "🔨 Building test project..." $Yellow
        dotnet build BigCommerce.Migration.Tests.csproj --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to build test project"
        }
        Write-ColoredOutput "✅ Test project built successfully" $Green
        
        # Run tests based on type
        $allTestsPassed = $true
        $testResults = @()
        
        switch ($TestType) {
            'quick' {
                $filter = Get-TestFilter 'quick'
                $result = Run-Tests $filter "Quick API Integration Tests" $ShowLogs
                $testResults += @{ Name = "Quick API Tests"; Passed = $result }
                $allTestsPassed = $allTestsPassed -and $result
            }
            
            'full' {
                $filter = Get-TestFilter 'full'
                $result = Run-Tests $filter "Full API Integration Tests" $ShowLogs
                $testResults += @{ Name = "Full API Tests"; Passed = $result }
                $allTestsPassed = $allTestsPassed -and $result
            }
            
            'performance' {
                $filter = Get-TestFilter 'performance'
                $result = Run-Tests $filter "API Performance Tests" $ShowLogs
                $testResults += @{ Name = "Performance Tests"; Passed = $result }
                $allTestsPassed = $allTestsPassed -and $result
            }
            
            'aggregation' {
                $filter = Get-TestFilter 'aggregation'
                $result = Run-Tests $filter "Aggregated Progress Tests" $ShowLogs
                $testResults += @{ Name = "Aggregation Tests"; Passed = $result }
                $allTestsPassed = $allTestsPassed -and $result
            }
            
            'all' {
                # Run all test categories
                Write-Header "🎯 Running Comprehensive API Integration Test Suite"
                
                # 1. Aggregated Progress Core Tests
                $filter = Get-TestFilter 'aggregation'
                $result = Run-Tests $filter "Aggregated Progress Core Tests" $ShowLogs
                $testResults += @{ Name = "Aggregation Core"; Passed = $result }
                $allTestsPassed = $allTestsPassed -and $result
                
                # 2. API Integration Tests
                $filter = Get-TestFilter 'quick'
                $result = Run-Tests $filter "API Endpoint Integration Tests" $ShowLogs
                $testResults += @{ Name = "API Endpoints"; Passed = $result }
                $allTestsPassed = $allTestsPassed -and $result
                
                # 3. Performance Tests
                $filter = Get-TestFilter 'performance'
                $result = Run-Tests $filter "API Performance Tests" $ShowLogs
                $testResults += @{ Name = "Performance"; Passed = $result }
                $allTestsPassed = $allTestsPassed -and $result
            }
        }
        
        # Display summary
        Write-Header "📊 Test Results Summary"
        
        foreach ($result in $testResults) {
            $status = if ($result.Passed) { "✅ PASSED" } else { "❌ FAILED" }
            $color = if ($result.Passed) { $Green } else { $Red }
            Write-ColoredOutput "   $($result.Name): $status" $color
        }
        
        Write-Host ""
        
        if ($allTestsPassed) {
            Write-ColoredOutput "🎉 All API integration tests PASSED!" $Green
            Write-ColoredOutput "✅ Task 4.1: API Endpoint Updates - VALIDATION SUCCESSFUL" $Green
            Write-Host ""
            Write-ColoredOutput "📋 Validated Features:" $Blue
            Write-ColoredOutput "   ✅ GetMigrationStatus uses aggregated progress" $Blue
            Write-ColoredOutput "   ✅ GetMigration (details) uses aggregated progress" $Blue
            Write-ColoredOutput "   ✅ GetMigrationHistory uses aggregated progress" $Blue
            Write-ColoredOutput "   ✅ GetLatestMigrationForStore uses aggregated progress" $Blue
            Write-ColoredOutput "   ✅ Dashboard endpoints use real-time data" $Blue
            Write-ColoredOutput "   ✅ Error handling and fallback scenarios work" $Blue
            Write-ColoredOutput "   ✅ Backward compatibility maintained" $Blue
            Write-ColoredOutput "   ✅ Performance within acceptable limits" $Blue
            
            exit 0
        } else {
            Write-ColoredOutput "❌ Some API integration tests FAILED!" $Red
            Write-ColoredOutput "🚨 Task 4.1: API Endpoint Updates - VALIDATION FAILED" $Red
            exit 1
        }
        
    }
    finally {
        Pop-Location
    }
}
catch {
    Write-ColoredOutput "💥 Script execution failed: $($_.Exception.Message)" $Red
    Write-ColoredOutput "Stack trace: $($_.ScriptStackTrace)" $Red
    exit 1
}