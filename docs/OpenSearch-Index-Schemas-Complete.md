# OpenSearch Index Schemas - Complete Migration System

## 📋 Document Overview

This document provides comprehensive OpenSearch index schemas for the BigCommerce Migration System, including all advanced features: migration history, status tracking, webhook notifications, scheduled migrations, and conflict resolution.

**Purpose:** Complete OpenSearch index schema reference  
**Audience:** Developers, DevOps, and system administrators  
**Related Documents:** OpenSearch-Logging-Strategy.md, Advanced-Features-and-Conflict-Resolution.md

---

## 🗂️ **Index Strategy and Naming Convention**

### **Index Pattern Structure**
All migration-related indices follow the pattern: `migration-{type}-{date}`

### **Daily Index Rotation**
- **Pattern**: `migration-{type}-{yyyy-MM-dd}`
- **Benefits**: Easier management, faster queries, automated lifecycle management
- **Retention**: Configurable per index type

### **Complete Index Types**

| Index Type | Pattern | Purpose | Retention | Size Estimate |
|------------|---------|---------|-----------|---------------|
| **Status** | `migration-status-{date}` | Real-time migration progress | 365 days | ~100MB/day |
| **Entities** | `migration-entities-{date}` | Entity-level processing details | 365 days | ~500MB/day |
| **Errors** | `migration-errors-{date}` | Error logs with context | 365 days | ~200MB/day |
| **Audit** | `migration-audit-{date}` | Complete audit trail | 7 years | ~50MB/day |
| **Payloads** | `migration-payloads-{date}` | Failed request/response data | 90 days | ~1GB/day |
| **Webhooks** | `migration-webhooks-{date}` | Webhook delivery logs | 90 days | ~25MB/day |
| **Schedules** | `migration-schedules-{date}` | Scheduled migration logs | 365 days | ~10MB/day |
| **Conflicts** | `migration-conflicts-{date}` | Conflict detection/resolution | 365 days | ~50MB/day |

---

## 📊 **Core Index Schemas**

### **1. Migration Status Index (`migration-status-{date}`)**

**Purpose**: Real-time migration progress tracking and status monitoring

```json
{
  "mappings": {
    "properties": {
      "migrationId": { "type": "keyword" },
      "status": { "type": "keyword" },
      "sourceStoreId": { "type": "keyword" },
      "destinationStoreId": { "type": "keyword" },
      "requestedBy": { "type": "keyword" },
      "startTime": { "type": "date" },
      "endTime": { "type": "date" },
      "lastUpdated": { "type": "date" },
      "currentPhase": { "type": "keyword" },
      "estimatedCompletion": { "type": "date" },
      "totalEntities": { "type": "long" },
      "completedEntities": { "type": "long" },
      "failedEntities": { "type": "long" },
      "skippedEntities": { "type": "long" },
      "percentComplete": { "type": "float" },
      "migrationMode": { "type": "keyword" },
      "configurationProfile": { "type": "keyword" },
      "isScheduled": { "type": "boolean" },
      "scheduleId": { "type": "keyword" },
      "entityBreakdown": {
        "type": "nested",
        "properties": {
          "entityType": { "type": "keyword" },
          "total": { "type": "long" },
          "processed": { "type": "long" },
          "failed": { "type": "long" },
          "skipped": { "type": "long" },
          "status": { "type": "keyword" },
          "startTime": { "type": "date" },
          "endTime": { "type": "date" },
          "processingTime": { "type": "float" }
        }
      },
      "performanceMetrics": {
        "properties": {
          "averageProcessingTime": { "type": "float" },
          "throughputPerSecond": { "type": "float" },
          "errorRate": { "type": "float" },
          "apiCallsPerSecond": { "type": "float" },
          "apiSuccessRate": { "type": "float" },
          "averageResponseTime": { "type": "float" }
        }
      },
      "conflictsSummary": {
        "properties": {
          "totalConflicts": { "type": "long" },
          "resolvedConflicts": { "type": "long" },
          "pendingConflicts": { "type": "long" },
          "autoResolvedConflicts": { "type": "long" },
          "manualResolvedConflicts": { "type": "long" }
        }
      },
      "webhooksSummary": {
        "properties": {
          "totalWebhooks": { "type": "long" },
          "successfulWebhooks": { "type": "long" },
          "failedWebhooks": { "type": "long" },
          "pendingWebhooks": { "type": "long" }
        }
      },
      "tags": { "type": "keyword" }
    }
  }
}
```

**Sample Document**:
```json
{
  "migrationId": "550e8400-e29b-41d4-a716-446655440000",
  "status": "InProgress",
  "sourceStoreId": "store-abc123",
  "destinationStoreId": "store-xyz789",
  "requestedBy": "user@company.com",
  "startTime": "2024-01-15T10:00:00Z",
  "lastUpdated": "2024-01-15T11:30:00Z",
  "currentPhase": "Processing Products",
  "estimatedCompletion": "2024-01-15T16:00:00Z",
  "totalEntities": 10000,
  "completedEntities": 3750,
  "failedEntities": 25,
  "skippedEntities": 15,
  "percentComplete": 37.5,
  "migrationMode": "Full",
  "configurationProfile": "Balanced",
  "isScheduled": false,
  "entityBreakdown": [
    {
      "entityType": "Categories",
      "total": 500,
      "processed": 500,
      "failed": 0,
      "skipped": 0,
      "status": "Completed",
      "startTime": "2024-01-15T10:00:00Z",
      "endTime": "2024-01-15T10:15:00Z",
      "processingTime": 900
    },
    {
      "entityType": "Products",
      "total": 8000,
      "processed": 3000,
      "failed": 20,
      "skipped": 10,
      "status": "InProgress",
      "startTime": "2024-01-15T10:15:00Z"
    }
  ],
  "performanceMetrics": {
    "averageProcessingTime": 2.3,
    "throughputPerSecond": 4.2,
    "errorRate": 0.006,
    "apiCallsPerSecond": 11.8,
    "apiSuccessRate": 99.4,
    "averageResponseTime": 245
  },
  "conflictsSummary": {
    "totalConflicts": 15,
    "resolvedConflicts": 12,
    "pendingConflicts": 3,
    "autoResolvedConflicts": 10,
    "manualResolvedConflicts": 2
  },
  "webhooksSummary": {
    "totalWebhooks": 8,
    "successfulWebhooks": 7,
    "failedWebhooks": 1,
    "pendingWebhooks": 0
  },
  "tags": ["production", "large-migration", "products"]
}
```

### **2. Migration Entities Index (`migration-entities-{date}`)**

**Purpose**: Entity-level processing details and component tracking

```json
{
  "mappings": {
    "properties": {
      "migrationId": { "type": "keyword" },
      "entityType": { "type": "keyword" },
      "entityId": { "type": "keyword" },
      "entityName": { "type": "text", "fields": { "keyword": { "type": "keyword" } } },
      "sourceId": { "type": "keyword" },
      "destinationId": { "type": "keyword" },
      "status": { "type": "keyword" },
      "startTime": { "type": "date" },
      "endTime": { "type": "date" },
      "processingTime": { "type": "float" },
      "batchId": { "type": "keyword" },
      "workerId": { "type": "keyword" },
      "complexity": { "type": "keyword" },
      "parentEntity": {
        "properties": {
          "type": { "type": "keyword" },
          "id": { "type": "keyword" },
          "name": { "type": "keyword" }
        }
      },
      "components": {
        "type": "nested",
        "properties": {
          "componentType": { "type": "keyword" },
          "componentId": { "type": "keyword" },
          "componentName": { "type": "keyword" },
          "sourceId": { "type": "keyword" },
          "destinationId": { "type": "keyword" },
          "status": { "type": "keyword" },
          "processingTime": { "type": "float" },
          "errorMessage": { "type": "text" },
          "retryCount": { "type": "integer" },
          "conflictId": { "type": "keyword" }
        }
      },
      "metadata": {
        "properties": {
          "sku": { "type": "keyword" },
          "upc": { "type": "keyword" },
          "price": { "type": "double" },
          "weight": { "type": "double" },
          "variantCount": { "type": "long" },
          "imageCount": { "type": "long" },
          "modifierCount": { "type": "long" },
          "categoryIds": { "type": "keyword" },
          "brandId": { "type": "keyword" }
        }
      },
      "configuration": {
        "properties": {
          "batchSize": { "type": "integer" },
          "maxConcurrency": { "type": "integer" },
          "priority": { "type": "integer" },
          "retryPolicy": { "type": "keyword" }
        }
      },
      "conflictResolution": {
        "properties": {
          "hasConflicts": { "type": "boolean" },
          "conflictIds": { "type": "keyword" },
          "resolutionStrategy": { "type": "keyword" },
          "resolutionStatus": { "type": "keyword" }
        }
      }
    }
  }
}
```

### **3. Migration Errors Index (`migration-errors-{date}`)**

**Purpose**: Error tracking with comprehensive context

```json
{
  "mappings": {
    "properties": {
      "errorId": { "type": "keyword" },
      "migrationId": { "type": "keyword" },
      "entityType": { "type": "keyword" },
      "entityId": { "type": "keyword" },
      "entityName": { "type": "keyword" },
      "sourceSystemId": { "type": "keyword" },
      "errorType": { "type": "keyword" },
      "errorCode": { "type": "keyword" },
      "errorMessage": { "type": "text" },
      "errorDetails": { "type": "text" },
      "stackTrace": { "type": "text", "index": false },
      "timestamp": { "type": "date" },
      "severity": { "type": "keyword" },
      "isResolved": { "type": "boolean" },
      "resolvedAt": { "type": "date" },
      "resolvedBy": { "type": "keyword" },
      "retryCount": { "type": "integer" },
      "maxRetries": { "type": "integer" },
      "nextRetryAt": { "type": "date" },
      "httpContext": {
        "properties": {
          "method": { "type": "keyword" },
          "url": { "type": "keyword" },
          "statusCode": { "type": "integer" },
          "responseHeaders": { "type": "object", "enabled": false },
          "requestHeaders": { "type": "object", "enabled": false }
        }
      },
      "businessContext": {
        "properties": {
          "operationType": { "type": "keyword" },
          "batchId": { "type": "keyword" },
          "workerId": { "type": "keyword" },
          "parentEntityId": { "type": "keyword" },
          "dependentEntityIds": { "type": "keyword" }
        }
      },
      "requestPayload": { "type": "text", "index": false },
      "responsePayload": { "type": "text", "index": false },
      "payloadSize": { "type": "long" },
      "tags": { "type": "keyword" }
    }
  }
}
```

---

## 🔔 **Advanced Features Index Schemas**

### **4. Webhooks Index (`migration-webhooks-{date}`)**

**Purpose**: Webhook delivery tracking and monitoring

```json
{
  "mappings": {
    "properties": {
      "webhookId": { "type": "keyword" },
      "migrationId": { "type": "keyword" },
      "storeId": { "type": "keyword" },
      "eventType": { "type": "keyword" },
      "eventId": { "type": "keyword" },
      "webhookUrl": { "type": "keyword" },
      "status": { "type": "keyword" },
      "timestamp": { "type": "date" },
      "deliveryAttempts": { "type": "integer" },
      "maxRetries": { "type": "integer" },
      "nextRetryAt": { "type": "date" },
      "firstAttemptAt": { "type": "date" },
      "lastAttemptAt": { "type": "date" },
      "successfulDeliveryAt": { "type": "date" },
      "totalDeliveryTime": { "type": "long" },
      "httpRequest": {
        "properties": {
          "method": { "type": "keyword" },
          "headers": { "type": "object", "enabled": false },
          "payloadSize": { "type": "long" },
          "contentType": { "type": "keyword" }
        }
      },
      "httpResponse": {
        "properties": {
          "statusCode": { "type": "integer" },
          "statusText": { "type": "keyword" },
          "headers": { "type": "object", "enabled": false },
          "responseTime": { "type": "long" },
          "responseSize": { "type": "long" }
        }
      },
      "errorDetails": {
        "properties": {
          "errorMessage": { "type": "text" },
          "errorType": { "type": "keyword" },
          "isRetryable": { "type": "boolean" },
          "retryAfter": { "type": "long" }
        }
      },
      "payload": { "type": "text", "index": false },
      "payloadHash": { "type": "keyword" },
      "tags": { "type": "keyword" }
    }
  }
}
```

**Sample Document**:
```json
{
  "webhookId": "webhook-001",
  "migrationId": "550e8400-e29b-41d4-a716-446655440000",
  "storeId": "store-abc123",
  "eventType": "migration.progress",
  "eventId": "evt-550e8400-e29b-41d4-a716-446655440001",
  "webhookUrl": "https://your-system.com/webhooks/migration-updates",
  "status": "Delivered",
  "timestamp": "2024-01-15T11:30:00Z",
  "deliveryAttempts": 1,
  "maxRetries": 3,
  "firstAttemptAt": "2024-01-15T11:30:00Z",
  "lastAttemptAt": "2024-01-15T11:30:00Z",
  "successfulDeliveryAt": "2024-01-15T11:30:05Z",
  "totalDeliveryTime": 5243,
  "httpRequest": {
    "method": "POST",
    "payloadSize": 1024,
    "contentType": "application/json"
  },
  "httpResponse": {
    "statusCode": 200,
    "statusText": "OK",
    "responseTime": 245,
    "responseSize": 58
  },
  "payloadHash": "a1b2c3d4e5f6",
  "tags": ["production", "progress-update"]
}
```

### **5. Scheduled Migrations Index (`migration-schedules-{date}`)**

**Purpose**: Scheduled migration execution tracking

```json
{
  "mappings": {
    "properties": {
      "scheduleExecutionId": { "type": "keyword" },
      "scheduleId": { "type": "keyword" },
      "scheduleName": { "type": "text", "fields": { "keyword": { "type": "keyword" } } },
      "migrationId": { "type": "keyword" },
      "cronExpression": { "type": "keyword" },
      "timezone": { "type": "keyword" },
      "scheduledTime": { "type": "date" },
      "actualStartTime": { "type": "date" },
      "completionTime": { "type": "date" },
      "status": { "type": "keyword" },
      "executionDuration": { "type": "long" },
      "delayFromScheduled": { "type": "long" },
      "sourceStoreId": { "type": "keyword" },
      "destinationStoreId": { "type": "keyword" },
      "entities": { "type": "keyword" },
      "migrationMode": { "type": "keyword" },
      "configurationProfile": { "type": "keyword" },
      "templateVariables": {
        "properties": {
          "lastRunDate": { "type": "date" },
          "currentRunDate": { "type": "date" },
          "dateRangeStart": { "type": "date" },
          "dateRangeEnd": { "type": "date" }
        }
      },
      "executionResult": {
        "properties": {
          "totalItems": { "type": "long" },
          "successfulItems": { "type": "long" },
          "failedItems": { "type": "long" },
          "skippedItems": { "type": "long" },
          "conflictsResolved": { "type": "long" },
          "successRate": { "type": "float" }
        }
      },
      "errorDetails": {
        "properties": {
          "errorMessage": { "type": "text" },
          "errorType": { "type": "keyword" },
          "retryCount": { "type": "integer" },
          "nextRetryAt": { "type": "date" },
          "maxRetries": { "type": "integer" }
        }
      },
      "previousExecution": {
        "properties": {
          "executionId": { "type": "keyword" },
          "completionTime": { "type": "date" },
          "status": { "type": "keyword" },
          "successRate": { "type": "float" }
        }
      },
      "nextExecution": {
        "properties": {
          "scheduledTime": { "type": "date" },
          "estimatedDuration": { "type": "long" }
        }
      },
      "tags": { "type": "keyword" }
    }
  }
}
```

### **6. Conflicts Index (`migration-conflicts-{date}`)**

**Purpose**: Conflict detection and resolution tracking

```json
{
  "mappings": {
    "properties": {
      "conflictId": { "type": "keyword" },
      "migrationId": { "type": "keyword" },
      "entityType": { "type": "keyword" },
      "entityId": { "type": "keyword" },
      "entityName": { "type": "text", "fields": { "keyword": { "type": "keyword" } } },
      "conflictType": { "type": "keyword" },
      "conflictField": { "type": "keyword" },
      "severity": { "type": "keyword" },
      "status": { "type": "keyword" },
      "detectedAt": { "type": "date" },
      "resolvedAt": { "type": "date" },
      "resolvedBy": { "type": "keyword" },
      "autoResolvable": { "type": "boolean" },
      "sourceEntity": {
        "properties": {
          "id": { "type": "keyword" },
          "name": { "type": "keyword" },
          "sku": { "type": "keyword" },
          "upc": { "type": "keyword" },
          "value": { "type": "text" },
          "metadata": { "type": "object", "enabled": false }
        }
      },
      "destinationEntity": {
        "properties": {
          "id": { "type": "keyword" },
          "name": { "type": "keyword" },
          "sku": { "type": "keyword" },
          "upc": { "type": "keyword" },
          "value": { "type": "text" },
          "metadata": { "type": "object", "enabled": false }
        }
      },
      "conflictDetails": {
        "properties": {
          "similarityScore": { "type": "float" },
          "matchingFields": { "type": "keyword" },
          "conflictingFields": { "type": "keyword" },
          "suggestedResolution": { "type": "keyword" },
          "resolutionReason": { "type": "text" }
        }
      },
      "resolution": {
        "properties": {
          "strategy": { "type": "keyword" },
          "action": { "type": "keyword" },
          "result": { "type": "keyword" },
          "newEntityId": { "type": "keyword" },
          "backupEntityId": { "type": "keyword" },
          "mergedEntityId": { "type": "keyword" },
          "resolutionDetails": { "type": "text" }
        }
      },
      "approvalRequired": { "type": "boolean" },
      "approvedBy": { "type": "keyword" },
      "approvedAt": { "type": "date" },
      "tags": { "type": "keyword" }
    }
  }
}
```

**Sample Document**:
```json
{
  "conflictId": "conflict-001",
  "migrationId": "550e8400-e29b-41d4-a716-446655440000",
  "entityType": "Products",
  "entityId": "12345",
  "entityName": "Blue Widget Pro",
  "conflictType": "DuplicateSku",
  "conflictField": "sku",
  "severity": "High",
  "status": "Resolved",
  "detectedAt": "2024-01-15T11:15:00Z",
  "resolvedAt": "2024-01-15T11:18:00Z",
  "resolvedBy": "AutoResolver",
  "autoResolvable": true,
  "sourceEntity": {
    "id": "12345",
    "name": "Blue Widget Pro",
    "sku": "BWP-001",
    "value": "Blue Widget Pro - Premium Model"
  },
  "destinationEntity": {
    "id": "67890",
    "name": "Blue Widget Pro V2",
    "sku": "BWP-001",
    "value": "Blue Widget Pro V2 - Updated Model"
  },
  "conflictDetails": {
    "similarityScore": 0.95,
    "matchingFields": ["sku", "category"],
    "conflictingFields": ["name", "price"],
    "suggestedResolution": "OverwriteExisting",
    "resolutionReason": "Source entity is newer and has updated specifications"
  },
  "resolution": {
    "strategy": "OverwriteExisting",
    "action": "ReplaceDestination",
    "result": "Success",
    "backupEntityId": "67890-backup",
    "resolutionDetails": "Destination entity backed up and replaced with source entity"
  },
  "approvalRequired": false,
  "tags": ["auto-resolved", "sku-conflict"]
}
```

### **7. Audit Trail Index (`migration-audit-{date}`)**

**Purpose**: Complete audit trail for compliance and debugging

```json
{
  "mappings": {
    "properties": {
      "auditId": { "type": "keyword" },
      "migrationId": { "type": "keyword" },
      "auditType": { "type": "keyword" },
      "timestamp": { "type": "date" },
      "requestedBy": { "type": "keyword" },
      "userRole": { "type": "keyword" },
      "clientIpAddress": { "type": "ip" },
      "userAgent": { "type": "text" },
      "sessionId": { "type": "keyword" },
      "requestId": { "type": "keyword" },
      "sourceStoreId": { "type": "keyword" },
      "destinationStoreId": { "type": "keyword" },
      "originalRequest": { "type": "text", "index": false },
      "processedRequest": { "type": "text", "index": false },
      "configurationProfile": { "type": "keyword" },
      "estimatedEntities": { "type": "long" },
      "estimatedDuration": { "type": "keyword" },
      "actualDuration": { "type": "long" },
      "validationResults": {
        "properties": {
          "isValid": { "type": "boolean" },
          "warnings": { "type": "text" },
          "errors": { "type": "text" },
          "recommendations": { "type": "text" }
        }
      },
      "securityContext": {
        "properties": {
          "authenticationMethod": { "type": "keyword" },
          "permissions": { "type": "keyword" },
          "accessLevel": { "type": "keyword" },
          "organizationId": { "type": "keyword" }
        }
      },
      "systemContext": {
        "properties": {
          "apiVersion": { "type": "keyword" },
          "systemVersion": { "type": "keyword" },
          "environment": { "type": "keyword" },
          "region": { "type": "keyword" },
          "functionAppName": { "type": "keyword" },
          "workerId": { "type": "keyword" }
        }
      },
      "businessContext": {
        "properties": {
          "businessUnit": { "type": "keyword" },
          "projectId": { "type": "keyword" },
          "costCenter": { "type": "keyword" },
          "department": { "type": "keyword" }
        }
      },
      "complianceData": {
        "properties": {
          "dataClassification": { "type": "keyword" },
          "retentionPolicy": { "type": "keyword" },
          "privacyLevel": { "type": "keyword" },
          "auditRequired": { "type": "boolean" }
        }
      },
      "tags": { "type": "keyword" }
    }
  }
}
```

---

## 🔍 **Query Examples for Advanced Features**

### **Webhook Performance Analysis**
```json
GET migration-webhooks-*/_search
{
  "query": {
    "bool": {
      "must": [
        { "term": { "storeId": "store-abc123" } },
        { "range": { "timestamp": { "gte": "2024-01-01", "lte": "2024-01-31" } } }
      ]
    }
  },
  "aggs": {
    "webhook_success_rate": {
      "terms": { "field": "status" }
    },
    "average_delivery_time": {
      "avg": { "field": "totalDeliveryTime" }
    },
    "failed_webhooks": {
      "filter": { "term": { "status": "Failed" } },
      "aggs": {
        "failure_reasons": {
          "terms": { "field": "errorDetails.errorType" }
        }
      }
    }
  }
}
```

### **Scheduled Migration History**
```json
GET migration-schedules-*/_search
{
  "query": {
    "bool": {
      "must": [
        { "term": { "scheduleId": "schedule-daily-products" } },
        { "range": { "scheduledTime": { "gte": "2024-01-01" } } }
      ]
    }
  },
  "sort": [
    { "scheduledTime": { "order": "desc" } }
  ],
  "aggs": {
    "execution_success_rate": {
      "terms": { "field": "status" }
    },
    "average_execution_duration": {
      "avg": { "field": "executionDuration" }
    },
    "schedule_reliability": {
      "avg": { "field": "delayFromScheduled" }
    }
  }
}
```

### **Conflict Resolution Analytics**
```json
GET migration-conflicts-*/_search
{
  "query": {
    "bool": {
      "must": [
        { "term": { "migrationId": "550e8400-e29b-41d4-a716-446655440000" } }
      ]
    }
  },
  "aggs": {
    "conflicts_by_type": {
      "terms": { "field": "conflictType" }
    },
    "conflicts_by_severity": {
      "terms": { "field": "severity" }
    },
    "resolution_strategies": {
      "terms": { "field": "resolution.strategy" }
    },
    "auto_resolution_rate": {
      "terms": { "field": "autoResolvable" }
    },
    "resolution_time": {
      "date_histogram": {
        "field": "resolvedAt",
        "interval": "1h"
      }
    }
  }
}
```

### **Migration Performance Dashboard**
```json
GET migration-status-*/_search
{
  "query": {
    "bool": {
      "must": [
        { "range": { "startTime": { "gte": "2024-01-01" } } },
        { "term": { "status": "Completed" } }
      ]
    }
  },
  "aggs": {
    "performance_by_profile": {
      "terms": { "field": "configurationProfile" },
      "aggs": {
        "avg_throughput": {
          "avg": { "field": "performanceMetrics.throughputPerSecond" }
        },
        "avg_success_rate": {
          "avg": { "field": "performanceMetrics.apiSuccessRate" }
        }
      }
    },
    "migrations_by_mode": {
      "terms": { "field": "migrationMode" }
    },
    "scheduled_vs_manual": {
      "terms": { "field": "isScheduled" }
    }
  }
}
```

---

## 🔄 **Index Lifecycle Management**

### **Index Templates**
```json
PUT _index_template/migration-status-template
{
  "index_patterns": ["migration-status-*"],
  "priority": 100,
  "template": {
    "settings": {
      "number_of_shards": 3,
      "number_of_replicas": 1,
      "index.lifecycle.name": "migration-status-policy",
      "index.lifecycle.rollover_alias": "migration-status"
    },
    "mappings": {
      // ... mapping from above
    }
  }
}
```

### **Lifecycle Policies**
```json
PUT _ilm/policy/migration-status-policy
{
  "policy": {
    "phases": {
      "hot": {
        "actions": {
          "rollover": {
            "max_size": "5GB",
            "max_age": "1d"
          }
        }
      },
      "warm": {
        "min_age": "7d",
        "actions": {
          "allocate": {
            "number_of_replicas": 0
          }
        }
      },
      "cold": {
        "min_age": "90d",
        "actions": {
          "allocate": {
            "number_of_replicas": 0
          }
        }
      },
      "delete": {
        "min_age": "365d"
      }
    }
  }
}
```

---

## 🎯 **Summary**

This comprehensive OpenSearch schema provides:

### **✅ Complete Migration Tracking**
- **Real-time Status**: Live migration progress and performance metrics
- **Entity-Level Details**: Individual item processing with component tracking
- **Error Management**: Comprehensive error context and resolution tracking
- **Audit Compliance**: 7-year audit trail for regulatory requirements

### **✅ Advanced Features Support**
- **Webhook Monitoring**: Delivery tracking with retry and failure analysis
- **Schedule Management**: Automated migration execution tracking
- **Conflict Resolution**: Intelligent duplicate detection and resolution audit
- **Performance Analytics**: Comprehensive dashboards and reporting

### **✅ Operational Excellence**
- **Scalable Architecture**: Daily index rotation with lifecycle management
- **Query Optimization**: Efficient search patterns and aggregations
- **Storage Efficiency**: Appropriate field types and indexing strategies
- **Compliance Ready**: Data classification and retention policies

**These schemas provide complete observability for your 10M+ product BigCommerce migrations with enterprise-grade logging, monitoring, and audit capabilities.**

---

**Document Version:** 1.0  
**Created:** January 2025  
**Includes:** Core + Advanced Features (Webhooks, Schedules, Conflicts) 