# Azure Durable Functions Deterministic Behavior Checker
# This script checks for common deterministic violations in orchestrator functions

param(
    [string[]]$Files = @(),
    [switch]$Fix = $false
)

$ErrorActionPreference = "Stop"

Write-Host "🔍 Checking Azure Durable Functions deterministic behavior..." -ForegroundColor Cyan

$violations = @()
$totalFiles = 0

# Define violation patterns
$violationPatterns = @{
    "BM0001" = @{
        Pattern = "DateTime\.(Now|UtcNow)"
        Message = "DateTime.{0} is non-deterministic. Use context.CurrentUtcDateTime instead"
        Severity = "Error"
        Fix = "context.CurrentUtcDateTime"
    }
    "BM0002" = @{
        Pattern = "Guid\.NewGuid\(\)"
        Message = "Guid.NewGuid() is non-deterministic. Use context.NewGuid() instead"
        Severity = "Error"
        Fix = "context.NewGuid()"
    }
    "BM0003" = @{
        Pattern = "Task\.Delay\(|Thread\.Sleep\("
        Message = "Task.Delay/Thread.Sleep is non-deterministic. Use context.CreateTimer() instead"
        Severity = "Error"
        Fix = "context.CreateTimer()"
    }
    "BM0004" = @{
        Pattern = "new Random\(\)"
        Message = "Random number generation is non-deterministic. Move to activity function"
        Severity = "Error"
        Fix = "// Move to activity function"
    }
    "BM0005" = @{
        Pattern = "HttpClient|\.GetAsync\(|\.PostAsync\(|\.PutAsync\(|\.DeleteAsync\("
        Message = "HTTP calls are non-deterministic. Move to activity function"
        Severity = "Error"
        Fix = "// Move to activity function"
    }
    "BM0006" = @{
        Pattern = "\.ConfigureAwait\(false\)"
        Message = "ConfigureAwait(false) can cause non-deterministic behavior"
        Severity = "Error"
        Fix = "// Remove ConfigureAwait(false)"
    }
}

# Get files to check
if ($Files.Count -eq 0) {
    $Files = Get-ChildItem -Path "." -Filter "*Orchestrator*.cs" -Recurse | Where-Object { 
        $_.FullName -notlike "*Tests*" -and $_.FullName -notlike "*bin\*" -and $_.FullName -notlike "*obj\*"
    } | ForEach-Object { $_.FullName }
}

Write-Host "📁 Checking $($Files.Count) orchestrator files..." -ForegroundColor Yellow

foreach ($file in $Files) {
    if (-not (Test-Path $file)) {
        Write-Warning "File not found: $file"
        continue
    }

    $totalFiles++
    $content = Get-Content $file -Raw
    $lines = Get-Content $file
    
    Write-Host "  🔍 Checking: $file" -ForegroundColor Gray
    
    $fileViolations = 0
    
    foreach ($ruleId in $violationPatterns.Keys) {
        $rule = $violationPatterns[$ruleId]
        
        if ($content -match $rule.Pattern) {
            $matches = [regex]::Matches($content, $rule.Pattern)
            
            foreach ($match in $matches) {
                # Find line number
                $lineNumber = ($content.Substring(0, $match.Index) -split "`n").Count
                $line = $lines[$lineNumber - 1]
                
                $violation = [PSCustomObject]@{
                    File = $file
                    RuleId = $ruleId
                    Line = $lineNumber
                    Column = $match.Index
                    Message = $rule.Message -f $match.Groups[1].Value
                    Severity = $rule.Severity
                    Code = $line.Trim()
                    Fix = $rule.Fix
                }
                
                $violations += $violation
                $fileViolations++
                
                Write-Host "    ❌ $ruleId`: $($violation.Message)" -ForegroundColor Red
                Write-Host "       Line $($violation.Line): $($violation.Code)" -ForegroundColor DarkRed
                
                if ($Fix) {
                    Write-Host "       💡 Suggested fix: $($rule.Fix)" -ForegroundColor Green
                }
            }
        }
    }
    
    if ($fileViolations -eq 0) {
        Write-Host "    ✅ No violations found" -ForegroundColor Green
    }
}

# Summary
Write-Host "" -ForegroundColor White
Write-Host "📊 Summary:" -ForegroundColor Cyan
Write-Host "  Files checked: $totalFiles" -ForegroundColor White
Write-Host "  Violations found: $($violations.Count)" -ForegroundColor White

if ($violations.Count -gt 0) {
    Write-Host "" -ForegroundColor White
    Write-Host "🚨 DETERMINISTIC VIOLATIONS FOUND!" -ForegroundColor Red
    Write-Host "" -ForegroundColor White
    Write-Host "Azure Durable Functions require deterministic behavior." -ForegroundColor Yellow
    Write-Host "Please fix these violations before committing:" -ForegroundColor Yellow
    Write-Host "" -ForegroundColor White
    
    # Group violations by rule
    $violationsByRule = $violations | Group-Object RuleId
    
    foreach ($ruleGroup in $violationsByRule) {
        Write-Host "Rule $($ruleGroup.Name):" -ForegroundColor Red
        foreach ($violation in $ruleGroup.Group) {
            Write-Host "  📄 $($violation.File):$($violation.Line) - $($violation.Message)" -ForegroundColor White
        }
        Write-Host "" -ForegroundColor White
    }
    
    Write-Host "📖 For more information, see:" -ForegroundColor Yellow
    Write-Host "   https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-code-constraints" -ForegroundColor Blue
    Write-Host "" -ForegroundColor White
    
    exit 1
} else {
    Write-Host "✅ All orchestrator functions are deterministic!" -ForegroundColor Green
    exit 0
} 