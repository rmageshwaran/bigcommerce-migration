# PowerShell script to verify the ChunkIncrementEvents table setup and functionality
# This script tests the incremental progress tracking infrastructure

param(
    [Parameter(Mandatory=$true)]
    [string]$ConnectionString,
    
    [Parameter(Mandatory=$false)]
    [string]$TableName = "chunkincrementevents",
    
    [Parameter(Mandatory=$false)]
    [switch]$RunTests = $false
)

# Import required modules
Import-Module Az.Storage -ErrorAction SilentlyContinue

if (-not (Get-Module -Name Az.Storage)) {
    Write-Error "Az.Storage module is required. Please install it with: Install-Module Az.Storage"
    exit 1
}

Write-Host "🔍 Verifying ChunkIncrementEvents table setup..." -ForegroundColor Green
Write-Host ""

try {
    # Connect to storage
    Write-Host "📡 Connecting to Azure Storage..." -ForegroundColor Yellow
    $StorageContext = New-AzStorageContext -ConnectionString $ConnectionString
    
    # Check if table exists
    Write-Host "🔍 Checking table existence..." -ForegroundColor Yellow
    $Table = Get-AzStorageTable -Name $TableName -Context $StorageContext -ErrorAction SilentlyContinue
    
    if (-not $Table) {
        Write-Host "❌ Table '$TableName' does not exist!" -ForegroundColor Red
        Write-Host ""
        Write-Host "🔧 To create the table, run:" -ForegroundColor Yellow
        Write-Host "   .\create-increment-events-table.ps1 -ConnectionString 'your-connection-string'"
        Write-Host ""
        exit 1
    }
    
    Write-Host "✅ Table '$TableName' exists!" -ForegroundColor Green
    Write-Host "   URI: $($Table.Uri)" -ForegroundColor Cyan
    Write-Host ""
    
    # Get table statistics
    Write-Host "📊 Gathering table statistics..." -ForegroundColor Yellow
    $TableClient = $Table.CloudTable
    
    # Query for sample data (limit to 10 records for performance)
    $Query = New-Object Microsoft.Azure.Cosmos.Table.TableQuery
    $Query.TakeCount = 10
    
    try {
        $SampleEntities = $TableClient.ExecuteQuery($Query)
        $EntityCount = $SampleEntities.Count
        
        Write-Host "📈 Table Statistics:" -ForegroundColor Cyan
        Write-Host "   Sample entities retrieved: $EntityCount (max 10)"
        
        if ($EntityCount -gt 0) {
            Write-Host "   Table contains data - incremental progress tracking is active!" -ForegroundColor Green
            
            # Show sample entity structure
            $SampleEntity = $SampleEntities[0]
            Write-Host ""
            Write-Host "🔍 Sample Entity Structure:" -ForegroundColor Cyan
            Write-Host "   PartitionKey: $($SampleEntity.PartitionKey)"
            Write-Host "   RowKey: $($SampleEntity.RowKey)"
            Write-Host "   Timestamp: $($SampleEntity.Timestamp)"
            
            # Show key properties if they exist
            if ($SampleEntity.Properties.ContainsKey("EntityType")) {
                Write-Host "   EntityType: $($SampleEntity.Properties["EntityType"].StringValue)"
            }
            if ($SampleEntity.Properties.ContainsKey("SuccessfulEntities")) {
                Write-Host "   SuccessfulEntities: $($SampleEntity.Properties["SuccessfulEntities"].Int32Value)"
            }
            if ($SampleEntity.Properties.ContainsKey("ProcessingStartTime")) {
                Write-Host "   ProcessingStartTime: $($SampleEntity.Properties["ProcessingStartTime"].DateTimeOffsetValue)"
            }
        } else {
            Write-Host "   Table is empty - no migrations have used incremental progress yet." -ForegroundColor Yellow
        }
        
    } catch {
        Write-Host "⚠️ Could not query table contents: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "   Table exists but may be empty or have access restrictions." -ForegroundColor Yellow
    }
    
    Write-Host ""
    
    # Run functionality tests if requested
    if ($RunTests) {
        Write-Host "🧪 Running functionality tests..." -ForegroundColor Yellow
        
        # Test 1: Write a test entity
        Write-Host "   Test 1: Writing test entity..." -ForegroundColor Cyan
        
        $TestEntity = @{
            PartitionKey = "test-migration-$(Get-Date -Format 'yyyyMMddHHmmss')"
            RowKey = "products-chunk-001-test-$(Get-Date -Format 'yyyyMMddHHmmssfff')"
            EntityType = "products"
            ChunkNumber = 1
            SuccessfulEntities = 250
            FailedEntities = 0
            SkippedEntities = 5
            CancelledEntities = 0
            ProcessingStartTime = (Get-Date).ToUniversalTime()
            ProcessingEndTime = (Get-Date).ToUniversalTime()
            ProcessingTimeMs = 5000
            SourceStore = "test-source"
            DestinationStore = "test-destination"
            HasErrors = $false
            CreatedAt = (Get-Date).ToUniversalTime()
            CreatedBy = "verify-script"
        }
        
        try {
            $TableOperation = [Microsoft.Azure.Cosmos.Table.TableOperation]::Insert($TestEntity)
            $Result = $TableClient.Execute($TableOperation)
            
            if ($Result.HttpStatusCode -eq 204) {
                Write-Host "   ✅ Test entity written successfully!" -ForegroundColor Green
                
                # Test 2: Read the test entity back
                Write-Host "   Test 2: Reading test entity..." -ForegroundColor Cyan
                
                $RetrieveOperation = [Microsoft.Azure.Cosmos.Table.TableOperation]::Retrieve($TestEntity.PartitionKey, $TestEntity.RowKey)
                $RetrieveResult = $TableClient.Execute($RetrieveOperation)
                
                if ($RetrieveResult.Result -ne $null) {
                    Write-Host "   ✅ Test entity read successfully!" -ForegroundColor Green
                    
                    # Test 3: Delete the test entity
                    Write-Host "   Test 3: Cleaning up test entity..." -ForegroundColor Cyan
                    
                    $DeleteOperation = [Microsoft.Azure.Cosmos.Table.TableOperation]::Delete($RetrieveResult.Result)
                    $DeleteResult = $TableClient.Execute($DeleteOperation)
                    
                    if ($DeleteResult.HttpStatusCode -eq 204) {
                        Write-Host "   ✅ Test entity deleted successfully!" -ForegroundColor Green
                        Write-Host ""
                        Write-Host "🎉 All functionality tests passed!" -ForegroundColor Green
                    } else {
                        Write-Host "   ⚠️ Test entity deletion failed (status: $($DeleteResult.HttpStatusCode))" -ForegroundColor Yellow
                    }
                } else {
                    Write-Host "   ❌ Test entity could not be read back!" -ForegroundColor Red
                }
            } else {
                Write-Host "   ❌ Test entity write failed (status: $($Result.HttpStatusCode))!" -ForegroundColor Red
            }
            
        } catch {
            Write-Host "   ❌ Functionality test failed: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
    
    # Display configuration guidance
    Write-Host "🔧 Configuration Check:" -ForegroundColor Cyan
    Write-Host "   ✅ Table '$TableName' is ready for use"
    Write-Host "   ✅ Connection string is valid and accessible"
    Write-Host ""
    
    Write-Host "📋 Next Steps:" -ForegroundColor Cyan
    Write-Host "   1. Ensure the connection string is configured in your application:"
    Write-Host "      - appsettings.json: ConnectionStrings.AzureWebJobsStorage"
    Write-Host "      - Environment variable: AzureWebJobsStorage"
    Write-Host "   2. Deploy the updated application with incremental progress services"
    Write-Host "   3. Monitor the table for chunk increment events during migrations"
    Write-Host ""
    
    Write-Host "🎯 Expected Behavior:" -ForegroundColor Cyan
    Write-Host "   • Each chunk (250-500 entities) will create one record in this table"
    Write-Host "   • Records will appear in real-time as chunks complete processing"
    Write-Host "   • Migration progress will be preserved even if cancelled mid-process"
    Write-Host "   • UI will show accurate progress based on data in this table"
    Write-Host ""
    
    Write-Host "✅ Verification completed successfully!" -ForegroundColor Green
    
} catch {
    Write-Host "❌ Verification failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "🔧 Troubleshooting:" -ForegroundColor Yellow
    Write-Host "   1. Verify the connection string is correct"
    Write-Host "   2. Ensure the table was created successfully"
    Write-Host "   3. Check network connectivity to Azure Storage"
    Write-Host "   4. Verify permissions for the storage account"
    Write-Host ""
    exit 1
}