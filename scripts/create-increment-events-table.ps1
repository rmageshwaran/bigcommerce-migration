# PowerShell script to create the ChunkIncrementEvents table in Azure Table Storage
# This script ensures the table exists and is properly configured for incremental progress tracking

param(
    [Parameter(Mandatory=$true)]
    [string]$ConnectionString,
    
    [Parameter(Mandatory=$false)]
    [string]$TableName = "chunkincrementevents",
    
    [Parameter(Mandatory=$false)]
    [switch]$Force = $false
)

# Import required modules
Import-Module Az.Storage -ErrorAction SilentlyContinue

if (-not (Get-Module -Name Az.Storage)) {
    Write-Error "Az.Storage module is required. Please install it with: Install-Module Az.Storage"
    exit 1
}

Write-Host "🚀 Creating ChunkIncrementEvents table for incremental progress tracking..." -ForegroundColor Green
Write-Host ""

try {
    # Parse connection string and create storage context
    Write-Host "📡 Connecting to Azure Storage..." -ForegroundColor Yellow
    $StorageContext = New-AzStorageContext -ConnectionString $ConnectionString
    
    # Check if table already exists
    Write-Host "🔍 Checking if table '$TableName' exists..." -ForegroundColor Yellow
    $ExistingTable = Get-AzStorageTable -Name $TableName -Context $StorageContext -ErrorAction SilentlyContinue
    
    if ($ExistingTable -and -not $Force) {
        Write-Host "✅ Table '$TableName' already exists. Use -Force to recreate." -ForegroundColor Green
        Write-Host ""
        Write-Host "📊 Table Information:" -ForegroundColor Cyan
        Write-Host "   Name: $($ExistingTable.Name)"
        Write-Host "   URI: $($ExistingTable.Uri)"
        Write-Host ""
        exit 0
    }
    
    if ($ExistingTable -and $Force) {
        Write-Host "🗑️ Force flag specified - deleting existing table..." -ForegroundColor Red
        Remove-AzStorageTable -Name $TableName -Context $StorageContext -Force
        Write-Host "⏳ Waiting for table deletion to complete..." -ForegroundColor Yellow
        Start-Sleep -Seconds 10
    }
    
    # Create the table
    Write-Host "🏗️ Creating table '$TableName'..." -ForegroundColor Yellow
    $Table = New-AzStorageTable -Name $TableName -Context $StorageContext
    
    Write-Host "✅ Successfully created table '$TableName'!" -ForegroundColor Green
    Write-Host ""
    
    # Display table information
    Write-Host "📊 Table Information:" -ForegroundColor Cyan
    Write-Host "   Name: $($Table.Name)"
    Write-Host "   URI: $($Table.Uri)"
    Write-Host ""
    
    # Display schema information
    Write-Host "🗄️ Table Schema:" -ForegroundColor Cyan
    Write-Host "   Partition Key: MigrationId (e.g., '47969ba9-ebba-49c2-ab09-131482562568')"
    Write-Host "   Row Key: ChunkId (e.g., 'products-chunk-001-20250117102345123-000001')"
    Write-Host ""
    Write-Host "📋 Key Fields:" -ForegroundColor Cyan
    Write-Host "   • MigrationId: Migration identifier"
    Write-Host "   • EntityType: Type of entity (products, categories, etc.)"
    Write-Host "   • ChunkNumber: Sequential chunk number"
    Write-Host "   • SuccessfulEntities: Successfully processed entities"
    Write-Host "   • FailedEntities: Failed entities"
    Write-Host "   • SkippedEntities: Skipped entities"
    Write-Host "   • CancelledEntities: Cancelled entities"
    Write-Host "   • ProcessingStartTime: When chunk processing started"
    Write-Host "   • ProcessingEndTime: When chunk processing completed"
    Write-Host ""
    
    Write-Host "🎯 Purpose:" -ForegroundColor Cyan
    Write-Host "   This table enables real-time progress tracking for BigCommerce migrations."
    Write-Host "   It prevents data loss when migrations are cancelled by storing progress"
    Write-Host "   immediately after each chunk (250-500 entities) completes processing."
    Write-Host ""
    
    Write-Host "✅ Table creation completed successfully!" -ForegroundColor Green
    Write-Host "   The incremental progress tracking system is now ready to use." -ForegroundColor Green
    
} catch {
    Write-Host "❌ Error creating table: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "🔧 Troubleshooting:" -ForegroundColor Yellow
    Write-Host "   1. Verify the connection string is correct"
    Write-Host "   2. Ensure you have permissions to create tables in the storage account"
    Write-Host "   3. Check that the Azure Storage account exists and is accessible"
    Write-Host "   4. Verify the Az.Storage PowerShell module is installed and up to date"
    Write-Host ""
    exit 1
}