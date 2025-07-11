# BigCommerce Migration System - Data Lifecycle and Cleanup Strategy

## Overview

This document defines comprehensive data lifecycle management and cleanup procedures for the BigCommerce migration system. It addresses cost optimization through automated cleanup processes while maintaining data integrity and compliance requirements.

## Table of Contents
1. [Data Retention Policies](#data-retention-policies)
2. [Cleanup Procedures by Data Type](#cleanup-procedures-by-data-type)
3. [Automated Cleanup Implementation](#automated-cleanup-implementation)
4. [Manual Cleanup Operations](#manual-cleanup-operations)
5. [Cost Optimization Strategies](#cost-optimization-strategies)
6. [Compliance and Audit Requirements](#compliance-and-audit-requirements)

## Data Retention Policies

### Default Retention Schedule

| Data Type | Retention Period | Reason | Cleanup Method |
|-----------|------------------|--------|----------------|
| **EntityMappings** | 30 days | Resume capability | Automated |
| **ApiCallTracking** | 7 days | Rate limiting only | Automated |
| **MigrationEntityConfig** | 90 days | Analysis & optimization | Automated |
| **CancellationTokens** | 7 days | Active cancellation only | Automated |
| **OpenSearch Logs** | 365 days | Compliance & debugging | Index lifecycle |
| **Failed Payloads** | 90 days | Debugging & investigation | Automated |
| **Audit Logs** | 7 years | Compliance requirement | Long-term storage |
| **CSV Reports** | 180 days | Business analysis | Automated |

### Configurable Retention Policies

```csharp
public class DataRetentionConfig
{
    public int EntityMappingsRetentionDays { get; set; } = 30;
    public int ApiCallTrackingRetentionDays { get; set; } = 7;
    public int MigrationConfigRetentionDays { get; set; } = 90;
    public int CancellationTokensRetentionDays { get; set; } = 7;
    public int FailedPayloadsRetentionDays { get; set; } = 90;
    public int CsvReportsRetentionDays { get; set; } = 180;
    public int AuditLogsRetentionYears { get; set; } = 7;
    public int OpenSearchLogsRetentionDays { get; set; } = 365;
    
    // Extended retention for specific scenarios
    public int ExtendedRetentionDays { get; set; } = 365;
    public bool EnableExtendedRetentionForFailedMigrations { get; set; } = true;
    public bool EnableExtendedRetentionForLargeMigrations { get; set; } = true;
    public int LargeMigrationThreshold { get; set; } = 1000000; // 1M entities
}
```

## Cleanup Procedures by Data Type

### 1. EntityMappings Table Cleanup

```csharp
public class EntityMappingsCleanupService
{
    private readonly ITableStorageService _tableStorage;
    private readonly ILogger<EntityMappingsCleanupService> _logger;
    private readonly DataRetentionConfig _retentionConfig;
    
    public async Task CleanupEntityMappings()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_retentionConfig.EntityMappingsRetentionDays);
        
        // Get all completed migrations older than retention period
        var oldMigrations = await GetCompletedMigrations(cutoffDate);
        
        foreach (var migration in oldMigrations)
        {
            // Check if migration qualifies for extended retention
            if (await ShouldExtendRetention(migration))
            {
                await MarkForExtendedRetention(migration);
                continue;
            }
            
            // Cleanup entity mappings for this migration
            await CleanupMigrationMappings(migration.MigrationId);
        }
    }
    
    private async Task CleanupMigrationMappings(string migrationId)
    {
        var entityTypes = new[] { "Categories", "Brands", "Products", "Customers", 
                                 "Variants", "Images", "Modifiers", "Options" };
        
        foreach (var entityType in entityTypes)
        {
            var partitionKey = $"{migrationId}:{entityType}";
            
            // Get all mappings for this migration/entity type
            var mappings = await _tableStorage.QueryAsync<EntityMappingEntity>(
                $"PartitionKey eq '{partitionKey}'");
            
            if (mappings.Any())
            {
                // Batch delete mappings
                await _tableStorage.BatchDeleteAsync("EntityMappings", mappings);
                
                _logger.LogInformation(
                    "Cleaned up {Count} entity mappings for {MigrationId}:{EntityType}",
                    mappings.Count(), migrationId, entityType);
            }
        }
    }
    
    private async Task<bool> ShouldExtendRetention(MigrationRecord migration)
    {
        // Extend retention for failed migrations
        if (_retentionConfig.EnableExtendedRetentionForFailedMigrations && 
            migration.Status == "Failed")
        {
            return true;
        }
        
        // Extend retention for large migrations
        if (_retentionConfig.EnableExtendedRetentionForLargeMigrations && 
            migration.TotalEntities > _retentionConfig.LargeMigrationThreshold)
        {
            return true;
        }
        
        return false;
    }
}
```

### 2. API Call Tracking Cleanup

```csharp
public class ApiCallTrackingCleanupService
{
    public async Task CleanupApiCallTracking()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_retentionConfig.ApiCallTrackingRetentionDays);
        
        // Clean up rate limiting buckets older than retention period
        var oldTrackingRecords = await _tableStorage.QueryAsync<ApiCallTracker>(
            $"Timestamp lt datetime'{cutoffDate:yyyy-MM-ddTHH:mm:ss}'");
        
        if (oldTrackingRecords.Any())
        {
            await _tableStorage.BatchDeleteAsync("ApiCallTracking", oldTrackingRecords);
            
            _logger.LogInformation(
                "Cleaned up {Count} API call tracking records older than {CutoffDate}",
                oldTrackingRecords.Count(), cutoffDate);
        }
    }
}
```

### 3. Migration Configuration Cleanup

```csharp
public class MigrationConfigCleanupService
{
    public async Task CleanupMigrationConfigurations()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_retentionConfig.MigrationConfigRetentionDays);
        
        // Clean up per-migration entity configurations
        var oldConfigs = await _tableStorage.QueryAsync<MigrationEntityConfigEntity>(
            $"Timestamp lt datetime'{cutoffDate:yyyy-MM-ddTHH:mm:ss}'");
        
        if (oldConfigs.Any())
        {
            await _tableStorage.BatchDeleteAsync("MigrationEntityConfig", oldConfigs);
            
            _logger.LogInformation(
                "Cleaned up {Count} migration configuration records",
                oldConfigs.Count());
        }
    }
}
```

### 4. Cancellation Tokens Cleanup

```csharp
public class CancellationTokensCleanupService
{
    public async Task CleanupCancellationTokens()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_retentionConfig.CancellationTokensRetentionDays);
        
        // Clean up old cancellation tokens
        var oldTokens = await _tableStorage.QueryAsync<CancellationTokenEntity>(
            $"Timestamp lt datetime'{cutoffDate:yyyy-MM-ddTHH:mm:ss}'");
        
        if (oldTokens.Any())
        {
            await _tableStorage.BatchDeleteAsync("CancellationTokens", oldTokens);
            await _tableStorage.BatchDeleteAsync("CancellationStatus", oldTokens);
            
            _logger.LogInformation(
                "Cleaned up {Count} cancellation token records",
                oldTokens.Count());
        }
    }
}
```

### 5. Blob Storage Cleanup

```csharp
public class BlobStorageCleanupService
{
    private readonly IBlobStorageService _blobStorage;
    
    public async Task CleanupCsvReports()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_retentionConfig.CsvReportsRetentionDays);
        
        // Clean up old CSV reports
        var oldReports = await _blobStorage.ListBlobsAsync("csv-reports", 
            blob => blob.LastModified < cutoffDate);
        
        foreach (var report in oldReports)
        {
            await _blobStorage.DeleteBlobAsync("csv-reports", report.Name);
        }
        
        _logger.LogInformation(
            "Cleaned up {Count} CSV report files older than {CutoffDate}",
            oldReports.Count(), cutoffDate);
    }
    
    public async Task CleanupLargePayloads()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_retentionConfig.FailedPayloadsRetentionDays);
        
        // Clean up old payload files
        var oldPayloads = await _blobStorage.ListBlobsAsync("large-payloads", 
            blob => blob.LastModified < cutoffDate);
        
        foreach (var payload in oldPayloads)
        {
            await _blobStorage.DeleteBlobAsync("large-payloads", payload.Name);
        }
        
        _logger.LogInformation(
            "Cleaned up {Count} large payload files older than {CutoffDate}",
            oldPayloads.Count(), cutoffDate);
    }
}
```

## Automated Cleanup Implementation

### 1. Scheduled Cleanup Function

```csharp
public class ScheduledCleanupFunction
{
    private readonly EntityMappingsCleanupService _mappingsCleanup;
    private readonly ApiCallTrackingCleanupService _apiCallCleanup;
    private readonly MigrationConfigCleanupService _configCleanup;
    private readonly CancellationTokensCleanupService _cancellationCleanup;
    private readonly BlobStorageCleanupService _blobCleanup;
    private readonly OpenSearchCleanupService _openSearchCleanup;
    
    [FunctionName("ScheduledCleanup")]
    public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo timer) // Daily at 2 AM
    {
        try
        {
            _logger.LogInformation("Starting automated cleanup process");
            
            // Run cleanup operations in parallel
            var cleanupTasks = new[]
            {
                _mappingsCleanup.CleanupEntityMappings(),
                _apiCallCleanup.CleanupApiCallTracking(),
                _configCleanup.CleanupMigrationConfigurations(),
                _cancellationCleanup.CleanupCancellationTokens(),
                _blobCleanup.CleanupCsvReports(),
                _blobCleanup.CleanupLargePayloads(),
                _openSearchCleanup.CleanupOldIndices()
            };
            
            await Task.WhenAll(cleanupTasks);
            
            _logger.LogInformation("Automated cleanup process completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automated cleanup process failed");
            throw;
        }
    }
}
```

### 2. OpenSearch Index Lifecycle Management

```csharp
public class OpenSearchCleanupService
{
    private readonly IOpenSearchService _openSearchService;
    
    public async Task CleanupOldIndices()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_retentionConfig.OpenSearchLogsRetentionDays);
        
        // Get all migration-related indices
        var indices = await _openSearchService.GetIndicesAsync("migration-*");
        
        foreach (var index in indices)
        {
            var indexDate = ExtractDateFromIndexName(index.Name);
            
            if (indexDate < cutoffDate)
            {
                await _openSearchService.DeleteIndexAsync(index.Name);
                
                _logger.LogInformation(
                    "Deleted OpenSearch index {IndexName} (Date: {IndexDate})",
                    index.Name, indexDate);
            }
        }
    }
    
    private DateTime ExtractDateFromIndexName(string indexName)
    {
        // Extract date from index name pattern: migration-status-2024-01-15
        var parts = indexName.Split('-');
        if (parts.Length >= 5)
        {
            var dateStr = $"{parts[^3]}-{parts[^2]}-{parts[^1]}";
            if (DateTime.TryParse(dateStr, out var date))
            {
                return date;
            }
        }
        
        return DateTime.MinValue;
    }
}
```

### 3. Cleanup Configuration Management

```csharp
public class CleanupConfigurationService
{
    public async Task UpdateRetentionPolicies(DataRetentionConfig config)
    {
        // Store updated retention configuration
        await _tableStorage.InsertOrReplaceAsync("SystemConfig", new
        {
            PartitionKey = "Cleanup",
            RowKey = "RetentionPolicy",
            Config = JsonSerializer.Serialize(config),
            UpdatedAt = DateTime.UtcNow
        });
        
        _logger.LogInformation("Updated data retention policies");
    }
    
    public async Task<DataRetentionConfig> GetRetentionPolicies()
    {
        var config = await _tableStorage.GetAsync<dynamic>("SystemConfig", "Cleanup", "RetentionPolicy");
        
        if (config?.Config != null)
        {
            return JsonSerializer.Deserialize<DataRetentionConfig>(config.Config);
        }
        
        return new DataRetentionConfig(); // Return default configuration
    }
}
```

## Manual Cleanup Operations

### 1. Migration-Specific Cleanup

```csharp
public class ManualCleanupService
{
    public async Task CleanupSpecificMigration(string migrationId, bool force = false)
    {
        var migration = await GetMigrationRecord(migrationId);
        
        if (migration == null)
        {
            throw new ArgumentException($"Migration {migrationId} not found");
        }
        
        // Check if migration is still active
        if (migration.Status == "InProgress" && !force)
        {
            throw new InvalidOperationException(
                $"Migration {migrationId} is still in progress. Use force=true to cleanup anyway.");
        }
        
        // Cleanup all data for this migration
        await CleanupMigrationData(migrationId);
        
        _logger.LogInformation(
            "Manually cleaned up all data for migration {MigrationId}",
            migrationId);
    }
    
    private async Task CleanupMigrationData(string migrationId)
    {
        var cleanupTasks = new[]
        {
            CleanupMigrationMappings(migrationId),
            CleanupMigrationConfigurations(migrationId),
            CleanupMigrationCancellationTokens(migrationId),
            CleanupMigrationBlobs(migrationId),
            CleanupMigrationLogs(migrationId)
        };
        
        await Task.WhenAll(cleanupTasks);
    }
}
```

### 2. Bulk Cleanup Operations

```csharp
public class BulkCleanupService
{
    public async Task CleanupCompletedMigrations(DateTime beforeDate)
    {
        var completedMigrations = await GetCompletedMigrations(beforeDate);
        
        _logger.LogInformation(
            "Starting bulk cleanup of {Count} completed migrations before {Date}",
            completedMigrations.Count(), beforeDate);
        
        foreach (var migration in completedMigrations)
        {
            try
            {
                await CleanupMigrationData(migration.MigrationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Failed to cleanup migration {MigrationId}",
                    migration.MigrationId);
            }
        }
    }
    
    public async Task CleanupFailedMigrations(DateTime beforeDate, bool preserveErrorData = true)
    {
        var failedMigrations = await GetFailedMigrations(beforeDate);
        
        foreach (var migration in failedMigrations)
        {
            if (preserveErrorData)
            {
                // Keep error logs and failed payloads for analysis
                await CleanupMigrationMappings(migration.MigrationId);
                await CleanupMigrationConfigurations(migration.MigrationId);
                await CleanupMigrationCancellationTokens(migration.MigrationId);
            }
            else
            {
                // Complete cleanup including error data
                await CleanupMigrationData(migration.MigrationId);
            }
        }
    }
}
```

## Cost Optimization Strategies

### 1. Storage Tier Management

```csharp
public class StorageTierOptimizationService
{
    public async Task OptimizeStorageTiers()
    {
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        var ninetyDaysAgo = DateTime.UtcNow.AddDays(-90);
        
        // Move older data to cooler storage tiers
        await MoveToArchiveStorage("csv-reports", thirtyDaysAgo);
        await MoveToArchiveStorage("large-payloads", ninetyDaysAgo);
        
        _logger.LogInformation("Completed storage tier optimization");
    }
    
    private async Task MoveToArchiveStorage(string containerName, DateTime cutoffDate)
    {
        var oldBlobs = await _blobStorage.ListBlobsAsync(containerName, 
            blob => blob.LastModified < cutoffDate);
        
        foreach (var blob in oldBlobs)
        {
            await _blobStorage.SetBlobTierAsync(containerName, blob.Name, "Archive");
        }
    }
}
```

### 2. Data Compression

```csharp
public class DataCompressionService
{
    public async Task CompressOldMappings(DateTime cutoffDate)
    {
        var oldMappings = await GetOldMappings(cutoffDate);
        
        foreach (var migrationGroup in oldMappings.GroupBy(m => m.MigrationId))
        {
            // Compress mappings into a single blob
            var compressedData = await CompressMappings(migrationGroup);
            
            // Store compressed data
            await _blobStorage.UploadBlobAsync("compressed-mappings", 
                $"{migrationGroup.Key}-mappings.gz", compressedData);
            
            // Remove original mappings
            await DeleteMappings(migrationGroup);
        }
    }
}
```

## Compliance and Audit Requirements

### 1. Audit Log Retention

```csharp
public class AuditLogRetentionService
{
    public async Task ManageAuditLogRetention()
    {
        var sevenYearsAgo = DateTime.UtcNow.AddYears(-_retentionConfig.AuditLogsRetentionYears);
        
        // Archive old audit logs instead of deleting
        await ArchiveOldAuditLogs(sevenYearsAgo);
        
        _logger.LogInformation(
            "Archived audit logs older than {Date} for compliance",
            sevenYearsAgo);
    }
    
    private async Task ArchiveOldAuditLogs(DateTime cutoffDate)
    {
        var oldLogs = await _openSearchService.QueryAsync("migration-audit-*", 
            $"timestamp:<{cutoffDate:yyyy-MM-ddTHH:mm:ss}");
        
        // Move to long-term archive storage
        await _blobStorage.UploadBlobAsync("audit-archive", 
            $"audit-logs-{cutoffDate:yyyy-MM}.json.gz", 
            await CompressLogs(oldLogs));
    }
}
```

### 2. Cleanup Audit Trail

```csharp
public class CleanupAuditService
{
    public async Task LogCleanupActivity(string operation, string details, int recordsAffected)
    {
        var auditRecord = new CleanupAuditRecord
        {
            Operation = operation,
            Details = details,
            RecordsAffected = recordsAffected,
            ExecutedAt = DateTime.UtcNow,
            ExecutedBy = "System"
        };
        
        await _openSearchService.IndexAsync("cleanup-audit", auditRecord);
    }
}
```

## Cleanup API Endpoints

### 1. Administrative Cleanup API

```csharp
[HttpPost("admin/cleanup")]
public async Task<IActionResult> TriggerCleanup([FromBody] CleanupRequest request)
{
    try
    {
        switch (request.CleanupType)
        {
            case "migration":
                await _manualCleanup.CleanupSpecificMigration(request.MigrationId, request.Force);
                break;
                
            case "bulk":
                await _bulkCleanup.CleanupCompletedMigrations(request.BeforeDate);
                break;
                
            case "failed":
                await _bulkCleanup.CleanupFailedMigrations(request.BeforeDate, request.PreserveErrorData);
                break;
                
            default:
                return BadRequest("Invalid cleanup type");
        }
        
        return Ok(new { message = "Cleanup operation completed successfully" });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Manual cleanup operation failed");
        return StatusCode(500, new { error = ex.Message });
    }
}
```

### 2. Cleanup Status API

```csharp
[HttpGet("admin/cleanup/status")]
public async Task<IActionResult> GetCleanupStatus()
{
    var status = new
    {
        LastCleanupRun = await GetLastCleanupRun(),
        NextScheduledCleanup = await GetNextScheduledCleanup(),
        StorageUtilization = await GetStorageUtilization(),
        RetentionPolicies = await _configService.GetRetentionPolicies(),
        PendingCleanupItems = await GetPendingCleanupItems()
    };
    
    return Ok(status);
}
```

## Summary

This comprehensive cleanup strategy provides:

1. **Automated Cleanup**: Daily scheduled cleanup of all data types
2. **Configurable Retention**: Flexible retention policies for different data types
3. **Cost Optimization**: Storage tier management and data compression
4. **Compliance**: Audit log retention and cleanup audit trails
5. **Manual Operations**: Administrative cleanup for specific scenarios
6. **Monitoring**: Cleanup status and utilization tracking

The system ensures optimal cost management while maintaining data integrity and compliance requirements. 