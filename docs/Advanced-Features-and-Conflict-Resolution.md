# Advanced Features and Conflict Resolution

## 📋 Document Overview

This document covers three critical advanced features for the BigCommerce Migration System:
1. **Webhook Notifications** - Real-time progress updates to external systems
2. **Scheduled Migrations** - Automated migrations on schedules
3. **Conflict Resolution** - Handling duplicate SKUs and conflicting data

**Purpose:** Technical specification for advanced migration features  
**Audience:** Developers, architects, and technical stakeholders  
**Related Documents:** Architecture-Documentation.md, Migration-Architecture-and-Execution-Flow.md

---

## 🔔 **Webhook Notifications System**

### **Architecture Overview**

Real-time webhook notifications provide external systems with live migration progress updates, enabling integration with business workflows, monitoring systems, and customer notifications.

### **Webhook Configuration**

#### **Store-Level Webhook Setup**
```json
{
  "storeId": "store-abc123",
  "webhookConfigurations": [
    {
      "webhookId": "webhook-001",
      "url": "https://your-system.com/webhooks/migration-updates",
      "events": ["migration.started", "migration.progress", "migration.completed", "migration.failed"],
      "headers": {
        "Authorization": "Bearer your-auth-token",
        "X-System-Id": "your-system-id"
      },
      "retryPolicy": {
        "maxRetries": 3,
        "retryDelaySeconds": [1, 5, 15]
      },
      "isActive": true
    },
    {
      "webhookId": "webhook-002", 
      "url": "https://monitoring.company.com/migration-alerts",
      "events": ["migration.failed", "migration.error"],
      "headers": {
        "X-API-Key": "monitoring-api-key"
      },
      "retryPolicy": {
        "maxRetries": 5,
        "retryDelaySeconds": [1, 2, 4, 8, 16]
      },
      "isActive": true
    }
  ]
}
```

#### **Webhook Event Types**
```csharp
public enum WebhookEventType
{
    MigrationStarted,
    MigrationProgress,
    MigrationCompleted,
    MigrationFailed,
    MigrationCancelled,
    MigrationPaused,
    MigrationResumed,
    EntityStarted,
    EntityCompleted,
    ConflictDetected,
    ConflictResolved,
    RateLimitExceeded,
    ErrorThresholdExceeded
}
```

### **Webhook Payload Structure**

#### **Migration Started Event**
```json
{
  "eventId": "evt-550e8400-e29b-41d4-a716-446655440000",
  "eventType": "migration.started",
  "timestamp": "2024-01-15T10:30:00Z",
  "migrationId": "550e8400-e29b-41d4-a716-446655440000",
  "sourceStoreId": "store-abc123",
  "destinationStoreId": "store-xyz789",
  "entities": ["Products", "Categories", "Brands"],
  "estimatedDuration": "PT2H30M",
  "totalItemsEstimate": 10000,
  "configuration": {
    "profile": "Balanced",
    "migrationMode": "Full"
  }
}
```

#### **Migration Progress Event**
```json
{
  "eventId": "evt-550e8400-e29b-41d4-a716-446655440001",
  "eventType": "migration.progress",
  "timestamp": "2024-01-15T11:00:00Z",
  "migrationId": "550e8400-e29b-41d4-a716-446655440000",
  "progress": {
    "totalItems": 10000,
    "processedItems": 2500,
    "failedItems": 5,
    "skippedItems": 10,
    "percentageComplete": 25.15
  },
  "currentEntity": "Products",
  "entityProgress": {
    "Products": {
      "total": 8000,
      "processed": 2000,
      "failed": 3,
      "percentageComplete": 25.0
    }
  },
  "performance": {
    "itemsPerSecond": 4.2,
    "averageResponseTime": 240,
    "apiSuccessRate": 99.85
  },
  "estimatedCompletion": "2024-01-15T14:30:00Z"
}
```

#### **Migration Completed Event**
```json
{
  "eventId": "evt-550e8400-e29b-41d4-a716-446655440002",
  "eventType": "migration.completed", 
  "timestamp": "2024-01-15T13:45:00Z",
  "migrationId": "550e8400-e29b-41d4-a716-446655440000",
  "duration": "PT3H15M",
  "summary": {
    "totalItems": 10000,
    "successfulItems": 9950,
    "failedItems": 25,
    "skippedItems": 25,
    "conflictsResolved": 15,
    "successRate": 99.5
  },
  "entitySummary": {
    "Categories": {"total": 500, "successful": 500, "failed": 0, "skipped": 0},
    "Brands": {"total": 200, "successful": 200, "failed": 0, "skipped": 0},
    "Products": {"total": 8000, "successful": 7950, "failed": 25, "skipped": 25},
    "Variants": {"total": 1300, "successful": 1300, "failed": 0, "skipped": 0}
  },
  "reportUrls": {
    "csvExport": "https://storage.com/reports/migration-550e8400-summary.csv",
    "errorReport": "https://storage.com/reports/migration-550e8400-errors.csv"
  }
}
```

### **Webhook Implementation**

#### **Azure Functions Webhook Service**
```csharp
[FunctionName("SendWebhookNotification")]
public async Task SendWebhookNotification(
    [ActivityTrigger] WebhookNotificationRequest request,
    ILogger log)
{
    var webhookConfigs = await _storeService.GetWebhookConfigurationsAsync(request.StoreId);
    
    foreach (var config in webhookConfigs.Where(w => w.IsActive && w.Events.Contains(request.EventType)))
    {
        await SendWebhookWithRetry(config, request.Payload, log);
    }
}

private async Task SendWebhookWithRetry(WebhookConfiguration config, object payload, ILogger log)
{
    var retryCount = 0;
    
    while (retryCount <= config.RetryPolicy.MaxRetries)
    {
        try
        {
            using var httpClient = new HttpClient();
            
            // Add configured headers
            foreach (var header in config.Headers)
            {
                httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
            }
            
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await httpClient.PostAsync(config.Url, content);
            
            if (response.IsSuccessStatusCode)
            {
                log.LogInformation("Webhook sent successfully to {Url}", config.Url);
                return;
            }
            
            log.LogWarning("Webhook failed with status {StatusCode}: {ReasonPhrase}", 
                response.StatusCode, response.ReasonPhrase);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Webhook send failed to {Url}", config.Url);
        }
        
        retryCount++;
        
        if (retryCount <= config.RetryPolicy.MaxRetries)
        {
            var delay = config.RetryPolicy.RetryDelaySeconds[retryCount - 1];
            await Task.Delay(TimeSpan.FromSeconds(delay));
        }
    }
    
    log.LogError("Webhook failed after {RetryCount} attempts to {Url}", 
        config.RetryPolicy.MaxRetries, config.Url);
}
```

#### **Integration with Migration Orchestrator**
```csharp
[FunctionName("MigrationOrchestrator")]
public async Task MigrationOrchestrator(
    [OrchestrationTrigger] IDurableOrchestrationContext context)
{
    var request = context.GetInput<MigrationRequest>();
    
    // Send migration started webhook
    await context.CallActivityAsync("SendWebhookNotification", new WebhookNotificationRequest
    {
        StoreId = request.SourceStoreId,
        EventType = WebhookEventType.MigrationStarted,
        Payload = new MigrationStartedEvent
        {
            MigrationId = request.MigrationId,
            SourceStoreId = request.SourceStoreId,
            DestinationStoreId = request.DestinationStoreId,
            Entities = request.Entities,
            EstimatedDuration = request.EstimatedDuration
        }
    });
    
    try
    {
        // Process migration...
        await ProcessMigrationEntities(context, request);
        
        // Send migration completed webhook
        await context.CallActivityAsync("SendWebhookNotification", new WebhookNotificationRequest
        {
            StoreId = request.SourceStoreId,
            EventType = WebhookEventType.MigrationCompleted,
            Payload = new MigrationCompletedEvent
            {
                MigrationId = request.MigrationId,
                Duration = context.CurrentUtcDateTime - request.StartTime,
                Summary = await GetMigrationSummary(request.MigrationId)
            }
        });
    }
    catch (Exception ex)
    {
        // Send migration failed webhook
        await context.CallActivityAsync("SendWebhookNotification", new WebhookNotificationRequest
        {
            StoreId = request.SourceStoreId,
            EventType = WebhookEventType.MigrationFailed,
            Payload = new MigrationFailedEvent
            {
                MigrationId = request.MigrationId,
                ErrorMessage = ex.Message,
                FailurePoint = context.CurrentUtcDateTime
            }
        });
        
        throw;
    }
}
```

---

## ⏰ **Scheduled Migrations System**

### **Architecture Overview**

Automated scheduled migrations enable regular synchronization between stores, such as nightly product updates, weekly inventory sync, or monthly full migrations.

### **Schedule Configuration**

#### **Store-Level Schedule Setup**
```json
{
  "storeId": "store-abc123",
  "schedules": [
    {
      "scheduleId": "schedule-daily-products",
      "name": "Daily Product Updates",
      "description": "Sync new and updated products daily at 2 AM",
      "cronExpression": "0 2 * * *",
      "timezone": "UTC",
      "isActive": true,
      "migrationTemplate": {
        "sourceStoreId": "store-abc123",
        "destinationStoreId": "store-xyz789",
        "entities": ["Products"],
        "migrationMode": "Incremental",
        "configuration": {
          "profile": "Conservative",
          "filters": {
            "dateRange": {
              "startDate": "{{lastRun}}",
              "endDate": "{{now}}"
            }
          }
        }
      },
      "webhookNotifications": true,
      "retryPolicy": {
        "maxRetries": 2,
        "retryDelayHours": [1, 4]
      }
    },
    {
      "scheduleId": "schedule-weekly-full",
      "name": "Weekly Full Sync",
      "description": "Complete migration every Sunday at 1 AM",
      "cronExpression": "0 1 * * 0",
      "timezone": "America/New_York",
      "isActive": true,
      "migrationTemplate": {
        "sourceStoreId": "store-abc123",
        "destinationStoreId": "store-xyz789",
        "entities": ["Products", "Categories", "Brands", "Customers"],
        "migrationMode": "Full",
        "configuration": {
          "profile": "Balanced"
        }
      },
      "webhookNotifications": true,
      "conflictResolution": {
        "strategy": "OverwriteExisting",
        "requireApproval": false
      }
    }
  ]
}
```

### **Azure Functions Timer Trigger Implementation**

#### **Schedule Manager Function**
```csharp
[FunctionName("ScheduledMigrationManager")]
public async Task ScheduledMigrationManager(
    [TimerTrigger("0 */5 * * * *")] TimerInfo timer, // Check every 5 minutes
    ILogger log)
{
    var currentTime = DateTime.UtcNow;
    var dueSchedules = await _scheduleService.GetDueSchedulesAsync(currentTime);
    
    foreach (var schedule in dueSchedules)
    {
        try
        {
            await ProcessScheduledMigration(schedule, currentTime, log);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to process scheduled migration {ScheduleId}", schedule.ScheduleId);
        }
    }
}

private async Task ProcessScheduledMigration(MigrationSchedule schedule, DateTime currentTime, ILogger log)
{
    // Check if previous migration is still running
    var lastExecution = await _scheduleService.GetLastExecutionAsync(schedule.ScheduleId);
    if (lastExecution?.Status == MigrationStatus.InProgress)
    {
        log.LogWarning("Skipping scheduled migration {ScheduleId} - previous execution still running", 
            schedule.ScheduleId);
        return;
    }
    
    // Create migration request from template
    var migrationRequest = await CreateMigrationFromTemplate(schedule.MigrationTemplate, schedule.ScheduleId);
    
    // Record execution start
    await _scheduleService.RecordExecutionStartAsync(schedule.ScheduleId, migrationRequest.MigrationId, currentTime);
    
    // Start migration
    await _migrationService.StartMigrationAsync(migrationRequest);
    
    log.LogInformation("Started scheduled migration {ScheduleId} with migration ID {MigrationId}", 
        schedule.ScheduleId, migrationRequest.MigrationId);
}
```

#### **Dynamic Template Processing**
```csharp
private async Task<MigrationRequest> CreateMigrationFromTemplate(
    MigrationTemplate template, 
    string scheduleId)
{
    var lastRun = await _scheduleService.GetLastSuccessfulRunAsync(scheduleId);
    var now = DateTime.UtcNow;
    
    // Replace template variables
    var processedTemplate = template.DeepClone();
    
    // Replace {{lastRun}} with last successful run date
    if (processedTemplate.Configuration?.Filters?.DateRange?.StartDate == "{{lastRun}}")
    {
        processedTemplate.Configuration.Filters.DateRange.StartDate = 
            lastRun?.CompletedAt?.ToString("O") ?? now.AddDays(-1).ToString("O");
    }
    
    // Replace {{now}} with current timestamp
    if (processedTemplate.Configuration?.Filters?.DateRange?.EndDate == "{{now}}")
    {
        processedTemplate.Configuration.Filters.DateRange.EndDate = now.ToString("O");
    }
    
    return new MigrationRequest
    {
        MigrationId = Guid.NewGuid().ToString(),
        SourceStoreId = processedTemplate.SourceStoreId,
        DestinationStoreId = processedTemplate.DestinationStoreId,
        Entities = processedTemplate.Entities,
        ScheduleId = scheduleId,
        IsScheduled = true,
        Configuration = processedTemplate.Configuration
    };
}
```

### **Schedule Management API**

#### **Create Schedule**
```csharp
[FunctionName("CreateSchedule")]
public async Task<IActionResult> CreateSchedule(
    [HttpTrigger(AuthorizationLevel.Function, "post", Route = "stores/{storeId}/schedules")] 
    HttpRequest req, string storeId)
{
    var schedule = await req.ReadFromJsonAsync<MigrationSchedule>();
    
    // Validate cron expression
    if (!CronExpression.IsValid(schedule.CronExpression))
    {
        return new BadRequestObjectResult("Invalid cron expression");
    }
    
    // Validate migration template
    var validationResult = await _migrationService.ValidateTemplateAsync(schedule.MigrationTemplate);
    if (!validationResult.IsValid)
    {
        return new BadRequestObjectResult(validationResult.Errors);
    }
    
    schedule.ScheduleId = Guid.NewGuid().ToString();
    schedule.StoreId = storeId;
    schedule.CreatedAt = DateTime.UtcNow;
    
    await _scheduleService.CreateScheduleAsync(schedule);
    
    return new OkObjectResult(schedule);
}
```

#### **Get Schedule History**
```csharp
[FunctionName("GetScheduleHistory")]
public async Task<IActionResult> GetScheduleHistory(
    [HttpTrigger(AuthorizationLevel.Function, "get", Route = "stores/{storeId}/schedules/{scheduleId}/history")] 
    HttpRequest req, string storeId, string scheduleId)
{
    var history = await _scheduleService.GetScheduleHistoryAsync(scheduleId);
    
    return new OkObjectResult(new
    {
        scheduleId,
        totalExecutions = history.Count,
        successfulExecutions = history.Count(h => h.Status == MigrationStatus.Completed),
        failedExecutions = history.Count(h => h.Status == MigrationStatus.Failed),
        lastExecution = history.OrderByDescending(h => h.StartedAt).FirstOrDefault(),
        nextExecution = CronExpression.Parse(schedule.CronExpression).GetNextOccurrence(DateTime.UtcNow),
        executions = history.OrderByDescending(h => h.StartedAt).Take(50)
    });
}
```

---

## ⚔️ **Conflict Resolution System**

### **Architecture Overview**

Comprehensive conflict resolution handles duplicate SKUs, conflicting product data, and entity relationships to ensure data integrity during migrations.

### **Conflict Detection**

#### **Conflict Types**
```csharp
public enum ConflictType
{
    DuplicateSku,
    DuplicateId,
    DuplicateName,
    CategoryMismatch,
    BrandMismatch,
    PriceMismatch,
    InventoryMismatch,
    ImageMismatch,
    VariantMismatch,
    CustomerEmailDuplicate,
    OrderNumberDuplicate,
    CustomFieldConflict
}
```

#### **Conflict Detection Logic**
```csharp
[FunctionName("DetectConflicts")]
public async Task<List<DataConflict>> DetectConflicts(
    [ActivityTrigger] ConflictDetectionRequest request,
    ILogger log)
{
    var conflicts = new List<DataConflict>();
    
    foreach (var entity in request.Entities)
    {
        switch (entity.EntityType)
        {
            case "Products":
                conflicts.AddRange(await DetectProductConflicts(entity, request.DestinationStoreId));
                break;
            case "Customers":
                conflicts.AddRange(await DetectCustomerConflicts(entity, request.DestinationStoreId));
                break;
            case "Categories":
                conflicts.AddRange(await DetectCategoryConflicts(entity, request.DestinationStoreId));
                break;
        }
    }
    
    return conflicts;
}

private async Task<List<DataConflict>> DetectProductConflicts(
    EntityData entity, 
    string destinationStoreId)
{
    var conflicts = new List<DataConflict>();
    var product = entity.Data.ToObject<Product>();
    
    // Check for duplicate SKU
    var existingBySku = await _bigCommerceService.GetProductBySkuAsync(destinationStoreId, product.Sku);
    if (existingBySku != null)
    {
        conflicts.Add(new DataConflict
        {
            ConflictId = Guid.NewGuid().ToString(),
            ConflictType = ConflictType.DuplicateSku,
            EntityType = "Products",
            SourceEntityId = product.Id.ToString(),
            DestinationEntityId = existingBySku.Id.ToString(),
            ConflictField = "Sku",
            SourceValue = product.Sku,
            DestinationValue = existingBySku.Sku,
            Severity = ConflictSeverity.High,
            AutoResolvable = true,
            DetectedAt = DateTime.UtcNow
        });
    }
    
    // Check for name conflicts
    var existingByName = await _bigCommerceService.GetProductByNameAsync(destinationStoreId, product.Name);
    if (existingByName != null && existingByName.Id != existingBySku?.Id)
    {
        conflicts.Add(new DataConflict
        {
            ConflictId = Guid.NewGuid().ToString(),
            ConflictType = ConflictType.DuplicateName,
            EntityType = "Products",
            SourceEntityId = product.Id.ToString(),
            DestinationEntityId = existingByName.Id.ToString(),
            ConflictField = "Name",
            SourceValue = product.Name,
            DestinationValue = existingByName.Name,
            Severity = ConflictSeverity.Medium,
            AutoResolvable = false,
            DetectedAt = DateTime.UtcNow
        });
    }
    
    return conflicts;
}
```

### **Conflict Resolution Strategies**

#### **Strategy Configuration**
```json
{
  "conflictResolutionStrategy": {
    "defaultStrategy": "SkipDuplicate",
    "entityStrategies": {
      "Products": {
        "DuplicateSku": {
          "strategy": "OverwriteExisting",
          "requireApproval": false,
          "backupOriginal": true
        },
        "DuplicateName": {
          "strategy": "CreateVariant",
          "requireApproval": true,
          "namingPattern": "{originalName} - Imported {timestamp}"
        },
        "PriceMismatch": {
          "strategy": "ManualReview",
          "requireApproval": true
        }
      },
      "Customers": {
        "CustomerEmailDuplicate": {
          "strategy": "MergeAccounts",
          "requireApproval": true,
          "preserveOrderHistory": true
        }
      }
    }
  }
}
```

#### **Resolution Strategy Implementation**
```csharp
public enum ConflictResolutionStrategy
{
    SkipDuplicate,          // Skip the conflicting item
    OverwriteExisting,      // Replace destination with source
    CreateVariant,          // Create new item with modified identifier
    MergeData,              // Intelligent merge of both items
    ManualReview,           // Queue for manual resolution
    PreserveNewest,         // Keep the most recently modified
    PreserveOldest,         // Keep the oldest item
    CreateRelationship      // Link items as variants/related
}

[FunctionName("ResolveConflicts")]
public async Task<ConflictResolutionResult> ResolveConflicts(
    [ActivityTrigger] ConflictResolutionRequest request,
    ILogger log)
{
    var results = new List<ConflictResolutionResult>();
    
    foreach (var conflict in request.Conflicts)
    {
        var strategy = GetResolutionStrategy(conflict, request.ResolutionConfig);
        var result = await ExecuteResolutionStrategy(conflict, strategy, log);
        results.Add(result);
    }
    
    return new ConflictResolutionResult
    {
        TotalConflicts = request.Conflicts.Count,
        ResolvedConflicts = results.Count(r => r.Success),
        FailedConflicts = results.Count(r => !r.Success),
        PendingManualReview = results.Count(r => r.RequiresManualReview),
        Results = results
    };
}

private async Task<ConflictResolutionResult> ExecuteResolutionStrategy(
    DataConflict conflict, 
    ConflictResolutionStrategy strategy, 
    ILogger log)
{
    switch (strategy)
    {
        case ConflictResolutionStrategy.SkipDuplicate:
            return await SkipDuplicateStrategy(conflict);
            
        case ConflictResolutionStrategy.OverwriteExisting:
            return await OverwriteExistingStrategy(conflict);
            
        case ConflictResolutionStrategy.CreateVariant:
            return await CreateVariantStrategy(conflict);
            
        case ConflictResolutionStrategy.MergeData:
            return await MergeDataStrategy(conflict);
            
        case ConflictResolutionStrategy.ManualReview:
            return await QueueManualReviewStrategy(conflict);
            
        default:
            throw new NotSupportedException($"Resolution strategy {strategy} not supported");
    }
}
```

### **Best Practices for Duplicate Handling**

#### **Recommended Approach: Multi-Layer Strategy**

**1. Prevention First**
```csharp
// Pre-migration validation
var duplicateCheck = await _validationService.CheckForDuplicatesAsync(
    sourceStoreId, destinationStoreId, entities);

if (duplicateCheck.HasDuplicates)
{
    // Present options to user before migration starts
    return new ConflictPreventionResult
    {
        PotentialConflicts = duplicateCheck.Conflicts,
        RecommendedActions = GenerateRecommendations(duplicateCheck.Conflicts),
        RequiresUserDecision = true
    };
}
```

**2. Intelligent Detection**
```csharp
// Smart duplicate detection beyond exact matches
public async Task<List<DataConflict>> DetectSmartDuplicates(Product product, string destinationStoreId)
{
    var conflicts = new List<DataConflict>();
    
    // Exact SKU match
    var exactSku = await _bigCommerceService.GetProductBySkuAsync(destinationStoreId, product.Sku);
    
    // Similar SKU (fuzzy matching)
    var similarSkus = await _bigCommerceService.GetProductsBySimilarSkuAsync(
        destinationStoreId, product.Sku, 0.8); // 80% similarity
    
    // UPC/EAN matching
    if (!string.IsNullOrEmpty(product.Upc))
    {
        var upcMatches = await _bigCommerceService.GetProductsByUpcAsync(destinationStoreId, product.Upc);
        conflicts.AddRange(CreateConflictsFromUpcMatches(product, upcMatches));
    }
    
    // Name similarity with intelligent matching
    var nameMatches = await _bigCommerceService.GetProductsBySimilarNameAsync(
        destinationStoreId, product.Name, 0.9); // 90% similarity
    
    return conflicts;
}
```

**3. Configurable Resolution Matrix**
```csharp
public class ConflictResolutionMatrix
{
    public Dictionary<ConflictType, ConflictResolutionStrategy> DefaultStrategies { get; set; } = new()
    {
        [ConflictType.DuplicateSku] = ConflictResolutionStrategy.OverwriteExisting,
        [ConflictType.DuplicateName] = ConflictResolutionStrategy.CreateVariant,
        [ConflictType.PriceMismatch] = ConflictResolutionStrategy.PreserveNewest,
        [ConflictType.InventoryMismatch] = ConflictResolutionStrategy.MergeData,
        [ConflictType.CustomerEmailDuplicate] = ConflictResolutionStrategy.MergeAccounts
    };
    
    public Dictionary<string, Dictionary<ConflictType, ConflictResolutionStrategy>> EntitySpecificStrategies { get; set; }
    
    public ConflictResolutionStrategy GetStrategy(string entityType, ConflictType conflictType)
    {
        if (EntitySpecificStrategies?.ContainsKey(entityType) == true &&
            EntitySpecificStrategies[entityType].ContainsKey(conflictType))
        {
            return EntitySpecificStrategies[entityType][conflictType];
        }
        
        return DefaultStrategies.GetValueOrDefault(conflictType, ConflictResolutionStrategy.ManualReview);
    }
}
```

#### **Recommended Duplicate Handling Strategies by Entity Type**

**Products:**
1. **SKU Duplicates**: `OverwriteExisting` (most common need)
2. **Name Duplicates**: `CreateVariant` with suffix
3. **Price Mismatches**: `PreserveNewest` or `ManualReview`
4. **Category Mismatches**: `MergeData` (combine categories)

**Customers:**
1. **Email Duplicates**: `MergeAccounts` (preserve order history)
2. **Name Duplicates**: `SkipDuplicate` (likely same person)
3. **Address Duplicates**: `MergeData` (combine addresses)

**Orders:**
1. **Order Number Duplicates**: `CreateVariant` with prefix
2. **Customer Mismatches**: `ManualReview` (critical for accuracy)

### **Manual Review Queue System**

#### **Manual Review Interface**
```csharp
[FunctionName("GetPendingConflicts")]
public async Task<IActionResult> GetPendingConflicts(
    [HttpTrigger(AuthorizationLevel.Function, "get", Route = "conflicts/pending")] 
    HttpRequest req)
{
    var pendingConflicts = await _conflictService.GetPendingConflictsAsync();
    
    return new OkObjectResult(new
    {
        totalPending = pendingConflicts.Count,
        highPriority = pendingConflicts.Count(c => c.Severity == ConflictSeverity.High),
        conflicts = pendingConflicts.Select(c => new
        {
            conflictId = c.ConflictId,
            conflictType = c.ConflictType,
            entityType = c.EntityType,
            sourceValue = c.SourceValue,
            destinationValue = c.DestinationValue,
            severity = c.Severity,
            detectedAt = c.DetectedAt,
            suggestedResolution = c.SuggestedResolution,
            requiresApproval = c.RequiresApproval
        })
    });
}

[FunctionName("ResolveConflictManually")]
public async Task<IActionResult> ResolveConflictManually(
    [HttpTrigger(AuthorizationLevel.Function, "post", Route = "conflicts/{conflictId}/resolve")] 
    HttpRequest req, string conflictId)
{
    var resolution = await req.ReadFromJsonAsync<ManualConflictResolution>();
    
    var result = await _conflictService.ResolveConflictAsync(conflictId, resolution);
    
    return new OkObjectResult(new
    {
        success = result.Success,
        message = result.Message,
        resolvedAt = DateTime.UtcNow,
        resolution = resolution.Strategy
    });
}
```

---

## 🎯 **Integration with Main Migration System**

### **Enhanced Migration Request Schema**

```json
{
  "sourceStoreId": "store-abc123",
  "destinationStoreId": "store-xyz789",
  "entities": ["Products", "Categories", "Brands"],
  "webhookNotifications": {
    "enabled": true,
    "events": ["migration.started", "migration.progress", "migration.completed"],
    "customWebhookUrl": "https://your-system.com/migration-webhook"
  },
  "conflictResolution": {
    "strategy": "Balanced",
    "autoResolve": true,
    "requireApprovalForHighSeverity": true
  },
  "scheduledMigration": {
    "isScheduled": false,
    "scheduleId": null
  }
}
```

### **Backend Processing Integration**

```csharp
[FunctionName("EnhancedMigrationOrchestrator")]
public async Task EnhancedMigrationOrchestrator(
    [OrchestrationTrigger] IDurableOrchestrationContext context)
{
    var request = context.GetInput<EnhancedMigrationRequest>();
    
    // 1. Send migration started webhook
    if (request.WebhookNotifications?.Enabled == true)
    {
        await context.CallActivityAsync("SendWebhookNotification", 
            CreateMigrationStartedWebhook(request));
    }
    
    // 2. Detect conflicts before migration
    var conflicts = await context.CallActivityAsync<List<DataConflict>>("DetectConflicts", request);
    
    if (conflicts.Any())
    {
        // 3. Resolve conflicts based on strategy
        var resolutionResult = await context.CallActivityAsync<ConflictResolutionResult>(
            "ResolveConflicts", new ConflictResolutionRequest
            {
                Conflicts = conflicts,
                ResolutionConfig = request.ConflictResolution
            });
        
        // 4. Send conflict detected webhook
        if (request.WebhookNotifications?.Enabled == true)
        {
            await context.CallActivityAsync("SendWebhookNotification", 
                CreateConflictDetectedWebhook(request, conflicts));
        }
        
        // 5. If manual review required, pause migration
        if (resolutionResult.PendingManualReview > 0)
        {
            await context.CallActivityAsync("PauseMigrationForManualReview", request.MigrationId);
            return;
        }
    }
    
    // 6. Proceed with migration
    await ProcessMigrationEntities(context, request);
    
    // 7. Send completion webhook
    if (request.WebhookNotifications?.Enabled == true)
    {
        await context.CallActivityAsync("SendWebhookNotification", 
            CreateMigrationCompletedWebhook(request));
    }
}
```

---

## 📊 **Performance and Scaling Considerations**

### **Webhook Performance**
- **Async Processing**: Webhooks sent asynchronously to avoid blocking migration
- **Batching**: Multiple events can be batched into single webhook call
- **Rate Limiting**: Webhook calls respect external system rate limits
- **Dead Letter Queue**: Failed webhooks stored for retry

### **Scheduling Performance**
- **Distributed Scheduling**: Multiple schedule manager instances for reliability
- **Overlap Prevention**: Prevent multiple instances of same scheduled migration
- **Resource Allocation**: Scheduled migrations use separate resource pools
- **Timezone Handling**: Accurate timezone conversion for global deployments

### **Conflict Resolution Performance**
- **Parallel Detection**: Conflicts detected in parallel with migration preparation
- **Caching**: Duplicate detection results cached for performance
- **Batch Resolution**: Multiple conflicts resolved in single operation
- **Smart Matching**: Fuzzy matching algorithms optimized for performance

---

## 🎯 **Summary**

These three advanced features transform the BigCommerce Migration System into a comprehensive enterprise solution:

### **Webhook Notifications**
✅ **Real-time Updates**: External systems receive live migration progress  
✅ **Flexible Configuration**: Per-store webhook configuration with retry policies  
✅ **Event-Driven**: Comprehensive event types for all migration phases  
✅ **Reliable Delivery**: Retry mechanisms and dead letter queue handling  

### **Scheduled Migrations**
✅ **Automated Sync**: Cron-based scheduling for regular migrations  
✅ **Template System**: Reusable migration templates with dynamic variables  
✅ **Conflict Prevention**: Overlap detection and retry policies  
✅ **Timezone Support**: Accurate scheduling across global deployments  

### **Conflict Resolution**
✅ **Intelligent Detection**: Smart duplicate detection beyond exact matches  
✅ **Configurable Strategies**: Multiple resolution strategies per conflict type  
✅ **Manual Review**: Human oversight for complex conflicts  
✅ **Complete Audit**: Full tracking of all conflict resolutions  

### **Recommended Duplicate Handling Strategy**
**Multi-Layer Approach:**
1. **Prevention**: Pre-migration duplicate detection and user consultation
2. **Intelligent Detection**: Fuzzy matching and multi-field comparison
3. **Configurable Resolution**: Entity-specific strategies with fallback to manual review
4. **Complete Audit**: Full tracking of all duplicate handling decisions

This architecture provides the **perfect balance** of automation and human oversight, ensuring data integrity while maintaining high throughput and reliability.

---

**Document Version:** 1.0  
**Created:** January 2025  
**Features:** Webhook Notifications, Scheduled Migrations, Conflict Resolution 