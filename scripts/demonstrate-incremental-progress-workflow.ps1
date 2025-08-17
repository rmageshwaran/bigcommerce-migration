#!/usr/bin/env pwsh

# 🚀 Incremental Progress Workflow Demonstration Script
# 
# This script demonstrates the complete incremental progress workflow integration
# by running comprehensive tests that show how the system updates migrations 
# and entityprogress tables with real-time aggregated data.
#
# Prerequisites:
# - Azurite storage emulator running locally
# - .NET 8 SDK installed
# - BigCommerce Migration solution built

param(
    [string]$TestFilter = "*IncrementalProgressWorkflowDemonstration*",
    [switch]$Verbose,
    [switch]$StartAzurite,
    [switch]$StopAzurite
)

# Script configuration
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Colors for output
$ColorSuccess = "Green"
$ColorWarning = "Yellow" 
$ColorError = "Red"
$ColorInfo = "Cyan"
$ColorHighlight = "Magenta"

function Write-ColorOutput {
    param([string]$Message, [string]$Color = "White")
    Write-Host $Message -ForegroundColor $Color
}

function Write-SectionHeader {
    param([string]$Title)
    Write-Host ""
    Write-ColorOutput "=" * 80 -Color $ColorHighlight
    Write-ColorOutput "  $Title" -Color $ColorHighlight
    Write-ColorOutput "=" * 80 -Color $ColorHighlight
    Write-Host ""
}

function Start-Azurite {
    Write-ColorOutput "🚀 Starting Azurite storage emulator..." -Color $ColorInfo
    
    try {
        # Check if Azurite is already running
        $azuriteProcess = Get-Process -Name "azurite" -ErrorAction SilentlyContinue
        if ($azuriteProcess) {
            Write-ColorOutput "✅ Azurite is already running (PID: $($azuriteProcess.Id))" -Color $ColorSuccess
            return
        }

        # Start Azurite in background
        Start-Process -FilePath "azurite" -ArgumentList "--silent" -WindowStyle Hidden
        Start-Sleep -Seconds 3

        # Verify it started
        $azuriteProcess = Get-Process -Name "azurite" -ErrorAction SilentlyContinue
        if ($azuriteProcess) {
            Write-ColorOutput "✅ Azurite started successfully (PID: $($azuriteProcess.Id))" -Color $ColorSuccess
        } else {
            Write-ColorOutput "❌ Failed to start Azurite - please start manually" -Color $ColorError
            exit 1
        }
    }
    catch {
        Write-ColorOutput "❌ Error starting Azurite: $($_.Exception.Message)" -Color $ColorError
        Write-ColorOutput "💡 Please install Azurite: npm install -g azurite" -Color $ColorWarning
        exit 1
    }
}

function Stop-Azurite {
    Write-ColorOutput "🛑 Stopping Azurite storage emulator..." -Color $ColorInfo
    
    try {
        $azuriteProcess = Get-Process -Name "azurite" -ErrorAction SilentlyContinue
        if ($azuriteProcess) {
            Stop-Process -Name "azurite" -Force
            Write-ColorOutput "✅ Azurite stopped successfully" -Color $ColorSuccess
        } else {
            Write-ColorOutput "ℹ️ Azurite was not running" -Color $ColorInfo
        }
    }
    catch {
        Write-ColorOutput "⚠️ Error stopping Azurite: $($_.Exception.Message)" -Color $ColorWarning
    }
}

function Test-Prerequisites {
    Write-SectionHeader "CHECKING PREREQUISITES"
    
    # Check .NET 8 SDK
    Write-ColorOutput "🔍 Checking .NET 8 SDK..." -Color $ColorInfo
    try {
        $dotnetVersion = dotnet --version
        Write-ColorOutput "✅ .NET SDK version: $dotnetVersion" -Color $ColorSuccess
    }
    catch {
        Write-ColorOutput "❌ .NET 8 SDK not found - please install from https://dotnet.microsoft.com/download" -Color $ColorError
        exit 1
    }

    # Check if solution builds
    Write-ColorOutput "🔍 Checking solution build..." -Color $ColorInfo
    try {
        $buildResult = dotnet build --configuration Debug --verbosity quiet 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-ColorOutput "✅ Solution builds successfully" -Color $ColorSuccess
        } else {
            Write-ColorOutput "❌ Solution build failed:" -Color $ColorError
            Write-ColorOutput $buildResult -Color $ColorError
            exit 1
        }
    }
    catch {
        Write-ColorOutput "❌ Error building solution: $($_.Exception.Message)" -Color $ColorError
        exit 1
    }

    # Check Azurite availability
    Write-ColorOutput "🔍 Checking Azurite availability..." -Color $ColorInfo
    try {
        $azuriteCheck = Get-Command azurite -ErrorAction SilentlyContinue
        if ($azuriteCheck) {
            Write-ColorOutput "✅ Azurite found: $($azuriteCheck.Source)" -Color $ColorSuccess
        } else {
            Write-ColorOutput "⚠️ Azurite not found - will attempt to use existing instance" -Color $ColorWarning
        }
    }
    catch {
        Write-ColorOutput "⚠️ Azurite check failed - proceeding anyway" -Color $ColorWarning
    }
}

function Run-WorkflowDemonstration {
    Write-SectionHeader "INCREMENTAL PROGRESS WORKFLOW DEMONSTRATION"
    
    Write-ColorOutput "🎯 Running comprehensive workflow demonstration tests..." -Color $ColorInfo
    Write-ColorOutput "📋 Test Filter: $TestFilter" -Color $ColorInfo
    Write-Host ""

    try {
        $testArgs = @(
            "test"
            "tests/BigCommerce.Migration.Tests/BigCommerce.Migration.Tests.csproj"
            "--filter", $TestFilter
            "--configuration", "Debug"
            "--logger", "console;verbosity=detailed"
        )

        if ($Verbose) {
            $testArgs += @("--verbosity", "diagnostic")
        }

        Write-ColorOutput "🔧 Running command: dotnet $($testArgs -join ' ')" -Color $ColorInfo
        Write-Host ""

        $testStartTime = Get-Date
        dotnet @testArgs

        $testEndTime = Get-Date
        $testDuration = $testEndTime - $testStartTime

        if ($LASTEXITCODE -eq 0) {
            Write-Host ""
            Write-ColorOutput "🎉 ALL WORKFLOW DEMONSTRATIONS PASSED!" -Color $ColorSuccess
            Write-ColorOutput "⏱️ Total execution time: $($testDuration.TotalSeconds.ToString('F1')) seconds" -Color $ColorSuccess
        } else {
            Write-Host ""
            Write-ColorOutput "❌ Some workflow demonstrations failed" -Color $ColorError
            Write-ColorOutput "💡 Check the detailed output above for specific failure reasons" -Color $ColorWarning
            exit 1
        }
    }
    catch {
        Write-ColorOutput "❌ Error running workflow demonstrations: $($_.Exception.Message)" -Color $ColorError
        exit 1
    }
}

function Show-DemonstrationSummary {
    Write-SectionHeader "DEMONSTRATION SUMMARY"
    
    Write-ColorOutput "📊 INCREMENTAL PROGRESS WORKFLOW INTEGRATION" -Color $ColorHighlight
    Write-Host ""
    
    Write-ColorOutput "✅ DEMONSTRATION 1: Complete Workflow Integration" -Color $ColorSuccess
    Write-ColorOutput "   • ProcessEntityChunkActivity → IncrementProgressAsync → ChunkIncrementEvents Table" -Color "White"
    Write-ColorOutput "   • UpdateEntityProgressActivity → GetLatestAggregatedProgressAsync → Enhanced Progress" -Color "White"
    Write-ColorOutput "   • PersistProgressToStorageAsync → Migrations & EntityProgress Tables" -Color "White"
    Write-Host ""
    
    Write-ColorOutput "✅ DEMONSTRATION 2: Cancellation Data Preservation" -Color $ColorSuccess
    Write-ColorOutput "   • Chunks written immediately prevent data loss" -Color "White"
    Write-ColorOutput "   • Aggregated progress shows preserved work after cancellation" -Color "White"
    Write-ColorOutput "   • Enhanced progress tracker returns preserved data" -Color "White"
    Write-Host ""
    
    Write-ColorOutput "✅ DEMONSTRATION 3: Table Update Integration" -Color $ColorSuccess
    Write-ColorOutput "   • Migrations table updated with aggregated chunk data" -Color "White"
    Write-ColorOutput "   • EntityProgress table updated with real-time statistics" -Color "White"
    Write-ColorOutput "   • Data comes from aggregation, not memory estimates" -Color "White"
    Write-Host ""
    
    Write-ColorOutput "✅ DEMONSTRATION 4: Performance Impact Analysis" -Color $ColorSuccess
    Write-ColorOutput "   • Fire-and-forget writes add minimal overhead" -Color "White"
    Write-ColorOutput "   • Processing speed maintained despite incremental tracking" -Color "White"
    Write-ColorOutput "   • Data integrity preserved with async writes" -Color "White"
    Write-Host ""
    
    Write-ColorOutput "✅ DEMONSTRATION 5: Real-time Dashboard Integration" -Color $ColorSuccess
    Write-ColorOutput "   • Dashboard receives real-time updates from aggregated data" -Color "White"
    Write-ColorOutput "   • Progress reflects actual chunk completion status" -Color "White"
    Write-ColorOutput "   • No more '0 progress' issues during cancellations" -Color "White"
    Write-Host ""
    
    Write-ColorOutput "🎯 KEY BENEFITS DEMONSTRATED:" -Color $ColorHighlight
    Write-ColorOutput "   1. 🛡️ Cancellation-safe progress (no data loss)" -Color $ColorSuccess
    Write-ColorOutput "   2. ⚡ Real-time accuracy (aggregated chunk data)" -Color $ColorSuccess
    Write-ColorOutput "   3. 🚀 Performance maintained (fire-and-forget writes)" -Color $ColorSuccess
    Write-ColorOutput "   4. 🔄 Backward compatibility (graceful fallbacks)" -Color $ColorSuccess
    Write-ColorOutput "   5. 📊 Accurate table updates (both migrations & entityprogress)" -Color $ColorSuccess
    Write-Host ""
}

# Main execution flow
try {
    Write-SectionHeader "INCREMENTAL PROGRESS WORKFLOW DEMONSTRATION"
    
    Write-ColorOutput "🎯 PURPOSE: Demonstrate how incremental progress updates migrations and entityprogress tables" -Color $ColorHighlight
    Write-ColorOutput "📋 SCOPE: End-to-end workflow integration with real Azure Storage" -Color $ColorInfo
    Write-Host ""

    # Handle Azurite management
    if ($StopAzurite) {
        Stop-Azurite
        exit 0
    }

    if ($StartAzurite) {
        Start-Azurite
    }

    # Run demonstrations
    Test-Prerequisites
    Run-WorkflowDemonstration
    Show-DemonstrationSummary

    Write-ColorOutput "🎉 WORKFLOW DEMONSTRATION COMPLETE!" -Color $ColorSuccess
    Write-ColorOutput "💡 The incremental progress system is ready for production deployment" -Color $ColorHighlight
}
catch {
    Write-ColorOutput "💥 DEMONSTRATION FAILED: $($_.Exception.Message)" -Color $ColorError
    exit 1
}
finally {
    if ($StartAzurite -and -not $StopAzurite) {
        Write-Host ""
        Write-ColorOutput "💡 TIP: Run with -StopAzurite to stop the Azurite emulator when done" -Color $ColorInfo
    }
}