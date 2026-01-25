# 🚀 ChunkIncrementEvents Table Setup Guide

## 📖 Overview

This guide helps you set up the ChunkIncrementEvents table in Azure Table Storage to enable incremental progress tracking for BigCommerce migrations.

**Purpose**: Fix the core problem where cancelled migrations show 0 progress in the UI when significant work was actually completed.

---

## 🎯 Problem Being Solved

### Before Incremental Progress:
```
Migration cancelled at 4,135/4,791 products processed
❌ UI Shows: Success: 0, Failed: 0, Processed: 0
✅ Reality: 4,135 products successfully migrated to BigCommerce
```

### After Incremental Progress:
```
Migration cancelled at 4,135/4,791 products processed  
✅ UI Shows: Success: 4,135, Failed: 656, Processed: 4,791
✅ Reality: 4,135 products successfully migrated to BigCommerce
```

---

## 🛠️ Setup Instructions

### Prerequisites
- Azure Storage Account with Table Storage enabled
- PowerShell with Az.Storage module installed
- Connection string to your Azure Storage Account

### Step 1: Install Required PowerShell Module
```powershell
Install-Module Az.Storage -Force -AllowClobber
```

### Step 2: Create the ChunkIncrementEvents Table
```powershell
# Navigate to the scripts directory
cd scripts

# Create the table (replace with your connection string)
.\create-increment-events-table.ps1 -ConnectionString "DefaultEndpointsProtocol=https;AccountName=youraccount;AccountKey=yourkey;EndpointSuffix=core.windows.net"
```

### Step 3: Verify the Setup
```powershell
# Verify the table was created successfully
.\verify-increment-events-setup.ps1 -ConnectionString "your-connection-string"

# Run functionality tests (optional)
.\verify-increment-events-setup.ps1 -ConnectionString "your-connection-string" -RunTests
```

---

## 📊 Table Schema

### Azure Table Storage Structure
```
Table Name: chunkincrementevents

Partition Key: MigrationId (e.g., "47969ba9-ebba-49c2-ab09-131482562568")
Row Key: ChunkId (e.g., "products-chunk-001-20250117102345123-000001")
```

### Key Fields
| Field | Type | Description |
|-------|------|-------------|
| **MigrationId** | string | Migration identifier (same as PartitionKey) |
| **EntityType** | string | Type of entity ("products", "categories", etc.) |
| **ChunkNumber** | int | Sequential chunk number (1, 2, 3, ...) |
| **SuccessfulEntities** | int | Successfully processed entities in this chunk |
| **FailedEntities** | int | Failed entities in this chunk |
| **SkippedEntities** | int | Skipped entities in this chunk |
| **CancelledEntities** | int | Cancelled entities in this chunk |
| **ProcessingStartTime** | DateTime | When chunk processing started |
| **ProcessingEndTime** | DateTime | When chunk processing completed |
| **ProcessingTimeMs** | long | Processing duration in milliseconds |
| **SourceStore** | string | Source BigCommerce store ID |
| **DestinationStore** | string | Destination BigCommerce store ID |

---

## 🔧 Configuration

### Application Configuration
Ensure your application has the Azure Storage connection string configured:

#### appsettings.json
```json
{
  "ConnectionStrings": {
    "AzureWebJobsStorage": "DefaultEndpointsProtocol=https;AccountName=youraccount;AccountKey=yourkey;EndpointSuffix=core.windows.net"
  }
}
```

#### Environment Variables
```bash
AzureWebJobsStorage="DefaultEndpointsProtocol=https;AccountName=youraccount;AccountKey=yourkey;EndpointSuffix=core.windows.net"
```

#### Azure Functions Configuration
The connection string should be the same one used for Azure Functions runtime.

---

## 📈 Expected Behavior

### During Migration Processing
1. **Chunk Processing**: Each chunk (250-500 entities) creates one record
2. **Real-Time Updates**: Records appear immediately after chunk completion
3. **Progress Aggregation**: UI queries table for real-time totals
4. **Cancellation Safety**: Progress preserved even if migration is cancelled

### Example Data Flow
```
Migration starts for 4,791 products:

Chunk 1 (Products 1-250):
├── Processing completes successfully
├── Record written: products-chunk-001-...
└── UI shows: 250 successful products

Chunk 2 (Products 251-500):  
├── Processing completes successfully
├── Record written: products-chunk-002-...
└── UI shows: 500 successful products

... (continues for all chunks)

User cancels migration at chunk 17:
├── 16 chunks completed = 4,000 products
├── Chunk 17 partially completed = 135 products
├── Records preserved in table
└── UI shows: 4,135 successful products
```

---

## 🧪 Testing the Setup

### Manual Testing
1. Run a small test migration (10-50 products)
2. Monitor the ChunkIncrementEvents table for new records
3. Cancel the migration mid-process
4. Verify the UI shows accurate progress

### Automated Testing
```powershell
# Run the verification script with tests
.\verify-increment-events-setup.ps1 -ConnectionString "your-connection-string" -RunTests
```

### Monitoring Queries
```sql
-- Query all chunks for a specific migration
SELECT * FROM chunkincrementevents 
WHERE PartitionKey = '47969ba9-ebba-49c2-ab09-131482562568'

-- Aggregate progress for a migration
SELECT 
    EntityType,
    COUNT(*) as TotalChunks,
    SUM(SuccessfulEntities) as TotalSuccessful,
    SUM(FailedEntities) as TotalFailed,
    SUM(SkippedEntities) as TotalSkipped
FROM chunkincrementevents 
WHERE PartitionKey = '47969ba9-ebba-49c2-ab09-131482562568'
GROUP BY EntityType
```

---

## 🚨 Troubleshooting

### Common Issues

#### Issue: "Table does not exist"
**Solution**: Run the table creation script:
```powershell
.\create-increment-events-table.ps1 -ConnectionString "your-connection-string"
```

#### Issue: "Connection string is invalid"
**Solution**: Verify your Azure Storage connection string:
- Check account name and key are correct
- Ensure the storage account exists and is accessible
- Test connectivity using Azure Storage Explorer

#### Issue: "No records appearing in table"
**Solution**: 
1. Verify the application is using the correct connection string
2. Check application logs for increment write errors
3. Ensure the incremental progress services are registered in DI
4. Verify migrations are processing chunks (not failing immediately)

#### Issue: "Permission denied"
**Solution**: 
- Ensure the connection string has read/write permissions
- Check Azure Storage access policies
- Verify the storage account is not behind a firewall

### Diagnostic Commands
```powershell
# Check if table exists
Get-AzStorageTable -Name "chunkincrementevents" -Context $StorageContext

# List all tables in storage account
Get-AzStorageTable -Context $StorageContext

# Check connection
Test-AzStorageAccount -ConnectionString "your-connection-string"
```

---

## 📋 Deployment Checklist

### Pre-Deployment
- [ ] Azure Storage Account configured and accessible
- [ ] ChunkIncrementEvents table created successfully
- [ ] Connection string configured in application settings
- [ ] Verification script tests pass

### Post-Deployment
- [ ] Monitor application logs for increment write success/failures
- [ ] Run test migration to verify real-time progress updates
- [ ] Test cancellation scenario to confirm progress preservation
- [ ] Set up monitoring alerts for increment write failures

### Monitoring
- [ ] Track increment write success rate
- [ ] Monitor table storage costs and usage
- [ ] Set up alerts for table access failures
- [ ] Monitor query performance for progress aggregation

---

## 🎉 Success Indicators

### Technical Success
- ✅ Table created without errors
- ✅ Test writes/reads succeed
- ✅ Application logs show successful increment writes
- ✅ Real-time progress updates in UI

### User Experience Success
- ✅ Users see progress updates every 2-3 seconds
- ✅ Cancelled migrations show actual completed work
- ✅ No more "0 progress" after successful work completion
- ✅ Users can resume migrations from where they left off

---

*Setup guide completed - ready for incremental progress tracking!*