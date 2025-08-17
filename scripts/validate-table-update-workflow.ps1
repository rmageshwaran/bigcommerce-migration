#!/usr/bin/env pwsh

# 🎯 Table Update Workflow Validation Script
# 
# This script provides a simple way to validate that the incremental progress
# workflow properly updates both the migrations and entityprogress tables
# with real-time aggregated data from chunk processing.

param(
    [switch]$QuickDemo,
    [switch]$FullDemo,
    [switch]$StartAzurite,
    [switch]$StopAzurite,
    [switch]$ShowLogs
)

$ErrorActionPreference = "Stop"

function Write-Header {
    param([string]$Title)
    Write-Host ""
    Write-Host "=" * 60 -ForegroundColor Magenta
    Write-Host "  $Title" -ForegroundColor Magenta  
    Write-Host "=" * 60 -ForegroundColor Magenta
    Write-Host ""
}

function Write-Step {
    param([string]$Step, [string]$Description)
    Write-Host "📋 $Step" -ForegroundColor Cyan
    Write-Host "   $Description" -ForegroundColor White
}

function Write-Success {
    param([string]$Message)
    Write-Host "✅ $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "⚠️ $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "❌ $Message" -ForegroundColor Red
}

function Start-AzuriteEmulator {
    Write-Step "STARTING AZURITE" "Starting local Azure Storage emulator"
    
    $azuriteProcess = Get-Process -Name "azurite" -ErrorAction SilentlyContinue
    if ($azuriteProcess) {
        Write-Success "Azurite already running (PID: $($azuriteProcess.Id))"
        return
    }

    try {
        Start-Process -FilePath "azurite" -ArgumentList "--silent" -WindowStyle Hidden
        Start-Sleep -Seconds 3
        
        $azuriteProcess = Get-Process -Name "azurite" -ErrorAction SilentlyContinue
        if ($azuriteProcess) {
            Write-Success "Azurite started (PID: $($azuriteProcess.Id))"
        } else {
            Write-Error "Failed to start Azurite"
            Write-Warning "Please install: npm install -g azurite"
            exit 1
        }
    }
    catch {
        Write-Error "Error starting Azurite: $($_.Exception.Message)"
        exit 1
    }
}

function Stop-AzuriteEmulator {
    Write-Step "STOPPING AZURITE" "Stopping local Azure Storage emulator"
    
    try {
        $azuriteProcess = Get-Process -Name "azurite" -ErrorAction SilentlyContinue
        if ($azuriteProcess) {
            Stop-Process -Name "azurite" -Force
            Write-Success "Azurite stopped"
        } else {
            Write-Warning "Azurite was not running"
        }
    }
    catch {
        Write-Warning "Error stopping Azurite: $($_.Exception.Message)"
    }
}

function Run-QuickDemo {
    Write-Header "QUICK WORKFLOW DEMONSTRATION"
    
    Write-Step "RUNNING" "Table Update Workflow Demonstration (focused test)"
    
    $testArgs = @(
        "test"
        "tests/BigCommerce.Migration.Tests/BigCommerce.Migration.Tests.csproj"
        "--filter", "DemonstrateCompleteTableUpdateWorkflow"
        "--configuration", "Debug"
        "--logger", "console;verbosity=normal"
    )

    if ($ShowLogs) {
        $testArgs += @("--verbosity", "detailed")
    }

    dotnet @testArgs

    if ($LASTEXITCODE -eq 0) {
        Write-Success "Quick demonstration completed successfully!"
        Write-Host ""
        Write-Host "💡 This test demonstrates:" -ForegroundColor Yellow
        Write-Host "   1. Chunks written immediately to ChunkIncrementEvents table" -ForegroundColor White
        Write-Host "   2. GetLatestAggregatedProgressAsync queries and aggregates chunk data" -ForegroundColor White
        Write-Host "   3. Migrations table updated with aggregated data (not memory estimates)" -ForegroundColor White
        Write-Host "   4. EntityProgress table updated with real-time chunk statistics" -ForegroundColor White
    } else {
        Write-Error "Quick demonstration failed"
        exit 1
    }
}

function Run-FullDemo {
    Write-Header "FULL WORKFLOW DEMONSTRATION SUITE"
    
    Write-Step "RUNNING" "Complete incremental progress workflow tests"
    
    $testArgs = @(
        "test"
        "tests/BigCommerce.Migration.Tests/BigCommerce.Migration.Tests.csproj"
        "--filter", "*IncrementalProgressWorkflowDemonstration*"
        "--configuration", "Debug"
        "--logger", "console;verbosity=detailed"
    )

    dotnet @testArgs

    if ($LASTEXITCODE -eq 0) {
        Write-Success "Full demonstration suite completed successfully!"
        Write-Host ""
        Write-Host "🎉 ALL WORKFLOW INTEGRATIONS VERIFIED:" -ForegroundColor Green
        Write-Host "   ✅ Complete workflow integration" -ForegroundColor Green
        Write-Host "   ✅ Cancellation data preservation" -ForegroundColor Green
        Write-Host "   ✅ Table update integration" -ForegroundColor Green
        Write-Host "   ✅ Performance impact analysis" -ForegroundColor Green
        Write-Host "   ✅ Real-time dashboard integration" -ForegroundColor Green
    } else {
        Write-Error "Full demonstration suite failed"
        exit 1
    }
}

function Show-WorkflowExplanation {
    Write-Header "INCREMENTAL PROGRESS WORKFLOW EXPLANATION"
    
    Write-Host "🔄 THE WORKFLOW YOU ASKED ABOUT:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "ProcessEntityChunkActivity" -ForegroundColor Cyan
    Write-Host "    ├─ (New) IncrementProgressAsync → ChunkIncrementEvents Table (immediate)" -ForegroundColor Green
    Write-Host "    └─ (Existing) UpdateEntityProgressActivity" -ForegroundColor White
    Write-Host "           └─ GetLatestAggregatedProgressAsync" -ForegroundColor White
    Write-Host "                ├─ Query ChunkIncrementEvents (real-time data)" -ForegroundColor Green
    Write-Host "                ├─ Aggregate by entity type" -ForegroundColor Green
    Write-Host "                ├─ Enhance cached progress" -ForegroundColor Green
    Write-Host "                └─ PersistProgressToStorageAsync" -ForegroundColor White
    Write-Host "                     ├─ Update Migrations Table (overall stats)" -ForegroundColor Yellow
    Write-Host "                     └─ Update EntityProgress Table (per-entity stats)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "🎯 KEY CHANGES:" -ForegroundColor Yellow
    Write-Host "   • Immediate chunk persistence prevents data loss" -ForegroundColor Green
    Write-Host "   • Table updates use aggregated real-time data" -ForegroundColor Green
    Write-Host "   • Fire-and-forget writes maintain performance" -ForegroundColor Green
    Write-Host "   • Backward compatibility preserved" -ForegroundColor Green
    Write-Host ""
}

# Main execution
try {
    if ($StopAzurite) {
        Stop-AzuriteEmulator
        exit 0
    }

    Show-WorkflowExplanation

    if ($StartAzurite) {
        Start-AzuriteEmulator
    }

    if ($QuickDemo) {
        Run-QuickDemo
    } elseif ($FullDemo) {
        Run-FullDemo
    } else {
        Write-Host "🚀 USAGE OPTIONS:" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "   Quick Demo:    ./validate-table-update-workflow.ps1 -QuickDemo" -ForegroundColor Cyan
        Write-Host "   Full Demo:     ./validate-table-update-workflow.ps1 -FullDemo" -ForegroundColor Cyan
        Write-Host "   Start Azurite: ./validate-table-update-workflow.ps1 -StartAzurite" -ForegroundColor Cyan
        Write-Host "   Stop Azurite:  ./validate-table-update-workflow.ps1 -StopAzurite" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "💡 RECOMMENDED: ./validate-table-update-workflow.ps1 -StartAzurite -QuickDemo" -ForegroundColor Green
    }
}
catch {
    Write-Error "Script failed: $($_.Exception.Message)"
    exit 1
}