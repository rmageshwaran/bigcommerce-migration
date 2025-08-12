# BigCommerce Migration System - OpenSearch Logging Strategy

## Overview

This document provides comprehensive documentation for the OpenSearch logging strategy in the BigCommerce migration system. It covers all index schemas, data models, query patterns, and retrieval strategies for complete observability and debugging capabilities.

## Table of Contents
1. [Index Strategy and Naming Convention](#index-strategy-and-naming-convention)
2. [Index Schemas and Data Models](#index-schemas-and-data-models)
3. [Logging Implementation](#logging-implementation)
4. [Query Examples and Retrieval Patterns](#query-examples-and-retrieval-patterns)
5. [Aggregation and Analytics Queries](#aggregation-and-analytics-queries)
6. [Index Lifecycle Management](#index-lifecycle-management)
7. [Performance Optimization](#performance-optimization)

## Index Strategy and Naming Convention

### Index Pattern Structure
All migration-related indices follow the pattern: `migration-{type}-{date}`

### Daily Index Rotation
- **Pattern**: `migration-{type}-{yyyy-MM-dd}`
- **Benefits**: Easier management, faster queries, automated lifecycle management
- **Retention**: Configurable per index type (default: 365 days)

### Index Types Overview

| Index Type | Pattern | Purpose | Retention | Size Estimate |
|------------|---------|---------|-----------|---------------|
| **Status** | `migration-status-{date}` | Real-time migration progress | 365 days | ~100MB/day |
| **Entities** | `migration-entities-{date}` | Entity-level processing details | 365 days | ~500MB/day |
| **Errors** | `migration-errors-{date}` | Error logs with context | 365 days | ~200MB/day |
| **Audit** | `migration-audit-{date}` | Complete audit trail | 7 years | ~50MB/day |
| **Payloads** | `migration-payloads-{date}` | Failed request/response data | 90 days | ~1GB/day |

## Index Schemas and Data Models

### 1. Migration Status Index (`migration-status-{date}`)

**Purpose**: Real-time migration progress tracking and status monitoring

```json
{
  "mappings": {
    "properties": {
      "migrationId": { "type": "keyword" },
      "status": { "type": "keyword" },
      "sourceStore": { "type": "keyword" },
      "destinationStore": { "type": "keyword" },
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
      "entityBreakdown": {
        "type": "nested",
        "properties": {
          "entityType": { "type": "keyword" },
          "total": { "type": "long" },
          "processed": { "type": "long" },
          "failed": { "type": "long" },
          "skipped": { "type": "long" },
          "status": { "type": "keyword" }
        }
      },
      "performanceMetrics": {
        "properties": {
          "averageProcessingTime": { "type": "float" },
          "throughputPerSecond": { "type": "float" },
          "errorRate": { "type": "float" },
          "apiCallsPerSecond": { "type": "float" }
        }
      },
      "configurationProfile": { "type": "keyword" },
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
  "sourceStore": "https://store-a.mybigcommerce.com",
  "destinationStore": "https://store-b.mybigcommerce.com",
  "requestedBy": "user@company.com",
  "startTime": "2024-01-15T10:00:00Z",
  "lastUpdated": "2024-01-15T12:30:00Z",
  "currentPhase": "Products",
  "estimatedCompletion": "2024-01-15T18:00:00Z",
  "totalEntities": 50000,
  "completedEntities": 12500,
  "failedEntities": 25,
  "skippedEntities": 5,
  "percentComplete": 25.0,
  "entityBreakdown": [
    {
      "entityType": "Categories",
      "total": 500,
      "processed": 500,
      "failed": 0,
      "skipped": 0,
      "status": "Completed"
    },
    {
      "entityType": "Products",
      "total": 10000,
      "processed": 2500,
      "failed": 25,
      "skipped": 5,
      "status": "InProgress"
    }
  ],
  "performanceMetrics": {
    "averageProcessingTime": 2.3,
    "throughputPerSecond": 1.7,
    "errorRate": 0.02,
    "apiCallsPerSecond": 8.5
  },
  "configurationProfile": "Balanced",
  "tags": ["large-migration", "prod-migration"]
}
```

### 2. Migration Entities Index (`migration-entities-{date}`)

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
          "errorMessage": { "type": "text" }
        }
      },
      "metadata": {
        "properties": {
          "complexity": { "type": "keyword" },
          "variantCount": { "type": "long" },
          "imageCount": { "type": "long" },
          "modifierCount": { "type": "long" },
          "optionCount": { "type": "long" }
        }
      },
      "configuration": {
        "properties": {
          "batchSize": { "type": "integer" },
          "maxConcurrency": { "type": "integer" },
          "priority": { "type": "integer" }
        }
      }
    }
  }
}
```

**Sample Document**:
```json
{
  "migrationId": "550e8400-e29b-41d4-a716-446655440000",
  "entityType": "Products",
  "entityId": "product-123",
  "entityName": "Wireless Bluetooth Headphones",
  "sourceId": "12345",
  "destinationId": "67890",
  "status": "Success",
  "startTime": "2024-01-15T10:30:00Z",
  "endTime": "2024-01-15T10:32:15Z",
  "processingTime": 135.5,
  "batchId": "batch-001",
  "workerId": "worker-003",
  "components": [
    {
      "componentType": "Variants",
      "componentId": "variant-001",
      "componentName": "Color: Black, Size: Medium",
      "sourceId": "var-123",
      "destinationId": "var-456",
      "status": "Success",
      "processingTime": 15.2
    },
    {
      "componentType": "Images",
      "componentId": "image-001",
      "componentName": "product-main.jpg",
      "sourceId": "img-123",
      "destinationId": "img-456",
      "status": "Success",
      "processingTime": 45.8
    }
  ],
  "metadata": {
    "complexity": "Medium",
    "variantCount": 12,
    "imageCount": 8,
    "modifierCount": 3,
    "optionCount": 2
  },
  "configuration": {
    "batchSize": 10,
    "maxConcurrency": 4,
    "priority": 2
  }
}
```

### 3. Migration Errors Index (`migration-errors-{date}`)

**Purpose**: Comprehensive error logging with full context

```json
{
  "mappings": {
    "properties": {
      "migrationId": { "type": "keyword" },
      "entityType": { "type": "keyword" },
      "entityId": { "type": "keyword" },
      "entityName": { "type": "text", "fields": { "keyword": { "type": "keyword" } } },
      "entitySourceId": { "type": "keyword" },
      "componentType": { "type": "keyword" },
      "componentId": { "type": "keyword" },
      "componentName": { "type": "keyword" },
      "errorLevel": { "type": "keyword" },
      "errorCode": { "type": "keyword" },
      "errorMessage": { "type": "text" },
      "errorDetails": { "type": "text" },
      "stackTrace": { "type": "text" },
      "timestamp": { "type": "date" },
      "apiEndpoint": { "type": "keyword" },
      "httpStatusCode": { "type": "integer" },
      "httpMethod": { "type": "keyword" },
      "requestPayload": { "type": "text", "index": false },
      "responsePayload": { "type": "text", "index": false },
      "requestHeaders": { "type": "object", "enabled": false },
      "responseHeaders": { "type": "object", "enabled": false },
      "context": {
        "properties": {
          "batchId": { "type": "keyword" },
          "workerId": { "type": "keyword" },
          "attemptNumber": { "type": "integer" },
          "processingPhase": { "type": "keyword" },
          "configurationUsed": { "type": "object" }
        }
      },
      "resolution": {
        "properties": {
          "status": { "type": "keyword" },
          "resolvedAt": { "type": "date" },
          "resolvedBy": { "type": "keyword" },
          "resolution": { "type": "text" }
        }
      }
    }
  }
}
```

**Sample Document**:
```json
{
  "migrationId": "550e8400-e29b-41d4-a716-446655440000",
  "entityType": "Products",
  "entityId": "product-123",
  "entityName": "Wireless Bluetooth Headphones",
  "entitySourceId": "12345",
  "componentType": "Variants",
  "componentId": "variant-001",
  "componentName": "Color: Black, Size: Medium",
  "errorLevel": "Error",
  "errorCode": "API_VALIDATION_ERROR",
  "errorMessage": "Invalid variant SKU format",
  "errorDetails": "SKU 'BT-HEAD-001' contains invalid characters. Only alphanumeric and dashes allowed.",
  "stackTrace": "...",
  "timestamp": "2024-01-15T10:31:45Z",
  "apiEndpoint": "/v3/catalog/products/12345/variants",
  "httpStatusCode": 422,
  "httpMethod": "POST",
  "requestPayload": "{\"sku\":\"BT-HEAD-001\",\"price\":\"99.99\"}",
  "responsePayload": "{\"errors\":[{\"field\":\"sku\",\"message\":\"Invalid format\"}]}",
  "context": {
    "batchId": "batch-001",
    "workerId": "worker-003",
    "attemptNumber": 1,
    "processingPhase": "Components",
    "configurationUsed": {
      "batchSize": 20,
      "maxConcurrency": 3
    }
  }
}
```

### 4. Migration Audit Index (`migration-audit-{date}`)

**Purpose**: Complete audit trail including migration request payloads

```json
{
  "mappings": {
    "properties": {
      "migrationId": { "type": "keyword" },
      "auditType": { "type": "keyword" },
      "timestamp": { "type": "date" },
      "requestedBy": { "type": "keyword" },
      "clientIpAddress": { "type": "ip" },
      "userAgent": { "type": "text" },
      "sessionId": { "type": "keyword" },
      "originalRequest": { "type": "object" },
      "configurationProfile": { "type": "keyword" },
      "estimatedEntities": { "type": "long" },
      "estimatedDuration": { "type": "keyword" },
      "validationResults": {
        "properties": {
          "isValid": { "type": "boolean" },
          "warnings": { "type": "text" },
          "errors": { "type": "text" }
        }
      },
      "apiVersion": { "type": "keyword" },
      "systemVersion": { "type": "keyword" },
      "businessUnit": { "type": "keyword" },
      "projectId": { "type": "keyword" },
      "tags": { "type": "keyword" },
      "changeDetails": {
        "properties": {
          "changeType": { "type": "keyword" },
          "previousValue": { "type": "object" },
          "newValue": { "type": "object" },
          "reason": { "type": "text" }
        }
      }
    }
  }
}
```

**Sample Document**:
```json
{
  "migrationId": "550e8400-e29b-41d4-a716-446655440000",
  "auditType": "MigrationStarted",
  "timestamp": "2024-01-15T10:00:00Z",
  "requestedBy": "user@company.com",
  "clientIpAddress": "192.168.1.100",
  "userAgent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)",
  "sessionId": "session-abc123",
  "originalRequest": {
    "sourceStore": "https://store-a.mybigcommerce.com",
    "destinationStore": "https://store-b.mybigcommerce.com",
    "entities": [
      {
        "entityType": "Products",
        "batchSize": 10,
        "maxConcurrency": 4,
        "filters": {
          "categories": [1, 2, 3]
        }
      }
    ]
  },
  "configurationProfile": "Balanced",
  "estimatedEntities": 50000,
  "estimatedDuration": "6-8 hours",
  "validationResults": {
    "isValid": true,
    "warnings": [],
    "errors": []
  },
  "apiVersion": "v1.0",
  "systemVersion": "2024.1.15",
  "businessUnit": "E-Commerce",
  "projectId": "PROJ-001",
  "tags": ["production", "large-migration"]
}
```

### 5. Migration Payloads Index (`migration-payloads-{date}`)

**Purpose**: Failed request/response payloads for debugging

```json
{
  "mappings": {
    "properties": {
      "migrationId": { "type": "keyword" },
      "entityType": { "type": "keyword" },
      "entityId": { "type": "keyword" },
      "entityName": { "type": "keyword" },
      "errorId": { "type": "keyword" },
      "requestPayload": { "type": "text", "index": false },
      "responsePayload": { "type": "text", "index": false },
      "payloadSize": { "type": "long" },
      "timestamp": { "type": "date" },
      "apiEndpoint": { "type": "keyword" },
      "httpMethod": { "type": "keyword" },
      "httpStatusCode": { "type": "integer" },
      "compressionType": { "type": "keyword" },
      "checksumMd5": { "type": "keyword" }
    }
  }
}
```

## Logging Implementation

### 1. OpenSearch Service Implementation

```csharp
public class OpenSearchService
{
    private readonly IOpenSearchClient _client;
    private readonly ILogger<OpenSearchService> _logger;
    
    public async Task IndexAsync<T>(string indexName, T document) where T : class
    {
        var dailyIndex = $"{indexName}-{DateTime.UtcNow:yyyy-MM-dd}";
        
        try
        {
            var response = await _client.IndexAsync(document, idx => idx.Index(dailyIndex));
            
            if (!response.IsValid)
            {
                _logger.LogError("Failed to index document to {Index}: {Error}", 
                    dailyIndex, response.DebugInformation);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while indexing to {Index}", dailyIndex);
        }
    }
    
    public async Task BulkIndexAsync<T>(string indexName, IEnumerable<T> documents) where T : class
    {
        var dailyIndex = $"{indexName}-{DateTime.UtcNow:yyyy-MM-dd}";
        
        var bulkRequest = new BulkRequest(dailyIndex)
        {
            Operations = documents.Select(doc => new BulkIndexOperation<T>(doc)).Cast<IBulkOperation>().ToList()
        };
        
        var response = await _client.BulkAsync(bulkRequest);
        
        if (response.HasErrors)
        {
            foreach (var error in response.ItemsWithErrors)
            {
                _logger.LogError("Bulk index error: {Error}", error.Error);
            }
        }
    }
}
```

### 2. Migration Logger Implementation

```csharp
public class MigrationLogger
{
    private readonly OpenSearchService _openSearchService;
    
    public async Task LogMigrationStatus(MigrationStatusLog status)
    {
        await _openSearchService.IndexAsync("migration-status", status);
    }
    
    public async Task LogEntityProcessing(EntityProcessingLog entity)
    {
        await _openSearchService.IndexAsync("migration-entities", entity);
    }
    
    public async Task LogError(MigrationErrorLog error)
    {
        await _openSearchService.IndexAsync("migration-errors", error);
        
        // Store large payloads separately
        if (error.RequestPayload?.Length > 10000 || error.ResponsePayload?.Length > 10000)
        {
            var payloadLog = new MigrationPayloadLog
            {
                MigrationId = error.MigrationId,
                EntityType = error.EntityType,
                EntityId = error.EntityId,
                ErrorId = error.Id,
                RequestPayload = error.RequestPayload,
                ResponsePayload = error.ResponsePayload,
                PayloadSize = (error.RequestPayload?.Length ?? 0) + (error.ResponsePayload?.Length ?? 0),
                Timestamp = DateTime.UtcNow
            };
            
            await _openSearchService.IndexAsync("migration-payloads", payloadLog);
        }
    }
    
    public async Task LogAuditTrail(MigrationAuditLog audit)
    {
        await _openSearchService.IndexAsync("migration-audit", audit);
    }
}
```

## Query Examples and Retrieval Patterns

### 1. Basic Migration Status Queries

```json
// Get current status of a specific migration
GET migration-status-*/_search
{
  "query": {
    "term": {
      "migrationId": "550e8400-e29b-41d4-a716-446655440000"
    }
  },
  "sort": [
    {
      "lastUpdated": {
        "order": "desc"
      }
    }
  ],
  "size": 1
}

// Get all active migrations
GET migration-status-*/_search
{
  "query": {
    "term": {
      "status": "InProgress"
    }
  },
  "sort": [
    {
      "startTime": {
        "order": "desc"
      }
    }
  ]
}

// Get migrations by user
GET migration-status-*/_search
{
  "query": {
    "term": {
      "requestedBy": "user@company.com"
    }
  },
  "sort": [
    {
      "startTime": {
        "order": "desc"
      }
    }
  ]
}
```

### 2. Entity-Level Queries

```json
// Get all entities for a migration
GET migration-entities-*/_search
{
  "query": {
    "term": {
      "migrationId": "550e8400-e29b-41d4-a716-446655440000"
    }
  },
  "sort": [
    {
      "startTime": {
        "order": "asc"
      }
    }
  ]
}

// Get failed entities
GET migration-entities-*/_search
{
  "query": {
    "bool": {
      "must": [
        {
          "term": {
            "migrationId": "550e8400-e29b-41d4-a716-446655440000"
          }
        },
        {
          "term": {
            "status": "Failed"
          }
        }
      ]
    }
  }
}

// Get entities by type and status
GET migration-entities-*/_search
{
  "query": {
    "bool": {
      "must": [
        {
          "term": {
            "migrationId": "550e8400-e29b-41d4-a716-446655440000"
          }
        },
        {
          "term": {
            "entityType": "Products"
          }
        },
        {
          "term": {
            "status": "Success"
          }
        }
      ]
    }
  }
}

// Complex products with many components
GET migration-entities-*/_search
{
  "query": {
    "bool": {
      "must": [
        {
          "term": {
            "entityType": "Products"
          }
        },
        {
          "range": {
            "metadata.variantCount": {
              "gte": 100
            }
          }
        }
      ]
    }
  }
}
```

### 3. Error Analysis Queries

```json
// Get all errors for a migration
GET migration-errors-*/_search
{
  "query": {
    "term": {
      "migrationId": "550e8400-e29b-41d4-a716-446655440000"
    }
  },
  "sort": [
    {
      "timestamp": {
        "order": "desc"
      }
    }
  ]
}

// Get errors by type and level
GET migration-errors-*/_search
{
  "query": {
    "bool": {
      "must": [
        {
          "term": {
            "migrationId": "550e8400-e29b-41d4-a716-446655440000"
          }
        },
        {
          "term": {
            "entityType": "Products"
          }
        },
        {
          "term": {
            "errorLevel": "Error"
          }
        }
      ]
    }
  }
}

// Get errors by HTTP status code
GET migration-errors-*/_search
{
  "query": {
    "bool": {
      "must": [
        {
          "term": {
            "migrationId": "550e8400-e29b-41d4-a716-446655440000"
          }
        },
        {
          "range": {
            "httpStatusCode": {
              "gte": 400,
              "lt": 500
            }
          }
        }
      ]
    }
  }
}

// Search errors by message content
GET migration-errors-*/_search
{
  "query": {
    "bool": {
      "must": [
        {
          "term": {
            "migrationId": "550e8400-e29b-41d4-a716-446655440000"
          }
        },
        {
          "match": {
            "errorMessage": "validation"
          }
        }
      ]
    }
  },
  "highlight": {
    "fields": {
      "errorMessage": {}
    }
  }
}
```

### 4. Audit Trail Queries

```json
// Get complete audit trail for a migration
GET migration-audit-*/_search
{
  "query": {
    "term": {
      "migrationId": "550e8400-e29b-41d4-a716-446655440000"
    }
  },
  "sort": [
    {
      "timestamp": {
        "order": "asc"
      }
    }
  ]
}

// Get migrations started by user in date range
GET migration-audit-*/_search
{
  "query": {
    "bool": {
      "must": [
        {
          "term": {
            "auditType": "MigrationStarted"
          }
        },
        {
          "term": {
            "requestedBy": "user@company.com"
          }
        },
        {
          "range": {
            "timestamp": {
              "gte": "2024-01-01T00:00:00Z",
              "lte": "2024-01-31T23:59:59Z"
            }
          }
        }
      ]
    }
  }
}

// Get configuration changes
GET migration-audit-*/_search
{
  "query": {
    "bool": {
      "must": [
        {
          "term": {
            "migrationId": "550e8400-e29b-41d4-a716-446655440000"
          }
        },
        {
          "term": {
            "auditType": "ConfigurationChanged"
          }
        }
      ]
    }
  }
}
```

### 5. Payload Queries

```json
// Get failed payloads for specific entity
GET migration-payloads-*/_search
{
  "query": {
    "bool": {
      "must": [
        {
          "term": {
            "migrationId": "550e8400-e29b-41d4-a716-446655440000"
          }
        },
        {
          "term": {
            "entityId": "product-123"
          }
        }
      ]
    }
  }
}

// Get large payloads (over 50KB)
GET migration-payloads-*/_search
{
  "query": {
    "range": {
      "payloadSize": {
        "gte": 51200
      }
    }
  },
  "sort": [
    {
      "payloadSize": {
        "order": "desc"
      }
    }
  ]
}
```

## Aggregation and Analytics Queries

### 1. Migration Performance Analytics

```json
// Migration success rates by entity type
GET migration-entities-*/_search
{
  "size": 0,
  "query": {
    "range": {
      "startTime": {
        "gte": "now-7d"
      }
    }
  },
  "aggs": {
    "by_entity_type": {
      "terms": {
        "field": "entityType",
        "size": 20
      },
      "aggs": {
        "by_status": {
          "terms": {
            "field": "status"
          }
        },
        "success_rate": {
          "bucket_script": {
            "buckets_path": {
              "success": "by_status['Success']>_count",
              "total": "_count"
            },
            "script": "params.success / params.total * 100"
          }
        }
      }
    }
  }
}

// Average processing time by entity type
GET migration-entities-*/_search
{
  "size": 0,
  "aggs": {
    "by_entity_type": {
      "terms": {
        "field": "entityType"
      },
      "aggs": {
        "avg_processing_time": {
          "avg": {
            "field": "processingTime"
          }
        }
      }
    }
  }
}

// Error distribution over time
GET migration-errors-*/_search
{
  "size": 0,
  "query": {
    "range": {
      "timestamp": {
        "gte": "now-24h"
      }
    }
  },
  "aggs": {
    "errors_over_time": {
      "date_histogram": {
        "field": "timestamp",
        "calendar_interval": "1h"
      },
      "aggs": {
        "by_error_level": {
          "terms": {
            "field": "errorLevel"
          }
        }
      }
    }
  }
}
```

### 2. System Performance Monitoring

```json
// API endpoint performance
GET migration-errors-*/_search
{
  "size": 0,
  "aggs": {
    "by_endpoint": {
      "terms": {
        "field": "apiEndpoint",
        "size": 50
      },
      "aggs": {
        "error_count": {
          "value_count": {
            "field": "apiEndpoint"
          }
        },
        "avg_response_time": {
          "avg": {
            "field": "context.responseTime"
          }
        }
      }
    }
  }
}

// Configuration profile effectiveness
GET migration-status-*/_search
{
  "size": 0,
  "aggs": {
    "by_profile": {
      "terms": {
        "field": "configurationProfile"
      },
      "aggs": {
        "avg_completion_time": {
          "avg": {
            "script": {
              "source": "doc['endTime'].value.millis - doc['startTime'].value.millis"
            }
          }
        },
        "avg_error_rate": {
          "avg": {
            "field": "performanceMetrics.errorRate"
          }
        }
      }
    }
  }
}
```

### 3. Business Intelligence Queries

```json
// Migration volume trends
GET migration-status-*/_search
{
  "size": 0,
  "query": {
    "range": {
      "startTime": {
        "gte": "now-30d"
      }
    }
  },
  "aggs": {
    "migrations_over_time": {
      "date_histogram": {
        "field": "startTime",
        "calendar_interval": "1d"
      },
      "aggs": {
        "total_entities": {
          "sum": {
            "field": "totalEntities"
          }
        },
        "avg_completion_time": {
          "avg": {
            "script": {
              "source": "doc['endTime'].value.millis - doc['startTime'].value.millis"
            }
          }
        }
      }
    }
  }
}

// User activity analysis
GET migration-audit-*/_search
{
  "size": 0,
  "query": {
    "term": {
      "auditType": "MigrationStarted"
    }
  },
  "aggs": {
    "by_user": {
      "terms": {
        "field": "requestedBy",
        "size": 50
      },
      "aggs": {
        "migration_count": {
          "value_count": {
            "field": "migrationId"
          }
        },
        "avg_entities_per_migration": {
          "avg": {
            "field": "estimatedEntities"
          }
        }
      }
    }
  }
}
```

## Index Lifecycle Management

### 1. Index Template Configuration

```json
PUT _index_template/migration-template
{
  "index_patterns": ["migration-*"],
  "template": {
    "settings": {
      "number_of_shards": 2,
      "number_of_replicas": 1,
      "index.lifecycle.name": "migration-lifecycle-policy",
      "index.lifecycle.rollover_alias": "migration-write"
    },
    "mappings": {
      "properties": {
        "@timestamp": {
          "type": "date"
        },
        "migrationId": {
          "type": "keyword"
        }
      }
    }
  }
}
```

### 2. Lifecycle Policy

```json
PUT _ilm/policy/migration-lifecycle-policy
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
          "shrink": {
            "number_of_shards": 1
          },
          "forcemerge": {
            "max_num_segments": 1
          }
        }
      },
      "cold": {
        "min_age": "30d",
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

### 3. Automated Cleanup Implementation

```csharp
public class OpenSearchCleanupService
{
    public async Task CleanupOldIndices()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-365);
        var indices = await _openSearchService.GetIndicesAsync("migration-*");
        
        foreach (var index in indices)
        {
            var indexDate = ExtractDateFromIndexName(index.Name);
            
            if (indexDate < cutoffDate)
            {
                await _openSearchService.DeleteIndexAsync(index.Name);
                _logger.LogInformation("Deleted index {IndexName}", index.Name);
            }
        }
    }
    
    private DateTime ExtractDateFromIndexName(string indexName)
    {
        // Extract date from migration-status-2024-01-15
        var parts = indexName.Split('-');
        if (parts.Length >= 5)
        {
            var dateStr = $"{parts[^3]}-{parts[^2]}-{parts[^1]}";
            return DateTime.TryParse(dateStr, out var date) ? date : DateTime.MinValue;
        }
        return DateTime.MinValue;
    }
}
```

## Performance Optimization

### 1. Search Optimization Strategies

```csharp
public class OptimizedOpenSearchQueries
{
    // Use filters instead of queries for exact matches
    public async Task<SearchResponse<MigrationStatusLog>> GetMigrationStatus(string migrationId)
    {
        return await _client.SearchAsync<MigrationStatusLog>(s => s
            .Index("migration-status-*")
            .Query(q => q
                .Bool(b => b
                    .Filter(f => f
                        .Term(t => t.Field(x => x.MigrationId).Value(migrationId))
                    )
                )
            )
            .Sort(so => so
                .Descending(x => x.LastUpdated)
            )
            .Size(1)
        );
    }
    
    // Use aggregations for analytics
    public async Task<SearchResponse<object>> GetErrorStatistics(string migrationId)
    {
        return await _client.SearchAsync<object>(s => s
            .Index("migration-errors-*")
            .Size(0)
            .Query(q => q
                .Term(t => t.Field("migrationId").Value(migrationId))
            )
            .Aggregations(a => a
                .Terms("by_entity_type", t => t
                    .Field("entityType")
                    .Aggregations(aa => aa
                        .Terms("by_error_level", tt => tt
                            .Field("errorLevel")
                        )
                    )
                )
            )
        );
    }
}
```

### 2. Bulk Operations

```csharp
public async Task BulkIndexEntities(IEnumerable<EntityProcessingLog> entities)
{
    var bulkDescriptor = new BulkDescriptor();
    
    foreach (var entity in entities)
    {
        var index = $"migration-entities-{DateTime.UtcNow:yyyy-MM-dd}";
        bulkDescriptor.Index<EntityProcessingLog>(i => i
            .Index(index)
            .Document(entity)
        );
    }
    
    var response = await _client.BulkAsync(bulkDescriptor);
    
    if (response.HasErrors)
    {
        foreach (var error in response.ItemsWithErrors)
        {
            _logger.LogError("Bulk index error: {Error}", error.Error);
        }
    }
}
```

### 3. Query Performance Tips

1. **Use index patterns**: `migration-status-2024-01-*` instead of `migration-status-*`
2. **Filter before query**: Use filters for exact matches, queries for full-text search
3. **Limit response size**: Use `size` parameter and pagination
4. **Use source filtering**: Only return needed fields with `_source`
5. **Cache frequent queries**: Implement application-level caching
6. **Use async operations**: All OpenSearch operations should be async

## Summary

This OpenSearch logging strategy provides:

1. **Comprehensive Schemas**: Complete data models for all migration aspects
2. **Flexible Querying**: Powerful query patterns for all use cases
3. **Performance Analytics**: Built-in aggregations for system monitoring
4. **Lifecycle Management**: Automated index management and cleanup
5. **Scalable Architecture**: Daily indices with efficient storage tiers
6. **Complete Observability**: Full traceability from request to completion

The strategy ensures optimal performance, cost-effectiveness, and complete visibility into all migration operations. 