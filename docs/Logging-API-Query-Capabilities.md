# BigCommerce Migration System - Logging API Query Capabilities

## Overview

This document provides comprehensive documentation for all logging API query capabilities in the BigCommerce migration system. It covers all available endpoints, query parameters, search patterns, and advanced filtering capabilities.

## Table of Contents
1. [Core Logging API Endpoints](#core-logging-api-endpoints)
2. [Query Parameters Reference](#query-parameters-reference)
3. [Search Patterns and Capabilities](#search-patterns-and-capabilities)
4. [Advanced Filtering Examples](#advanced-filtering-examples)
5. [Pagination and Performance](#pagination-and-performance)
6. [Error Payload Retrieval](#error-payload-retrieval)
7. [Response Format Reference](#response-format-reference)

## Core Logging API Endpoints

### 1. Get Logs - Primary Query Endpoint

**Endpoint**: `GET /api/logs`

**Purpose**: Retrieve migration logs with comprehensive filtering and search capabilities

**Authentication**: Required (API Key or Development bypass)

**Base URL**: `http://localhost:7071/api/logs`

### 2. Get Error Payload - Blob Storage Retrieval

**Endpoint**: `GET /api/logs/payload/{migrationId}/{requestId}/{payloadType}/{entityName}`

**Purpose**: Retrieve detailed error payloads from blob storage

**Authentication**: Required (API Key or Development bypass)

### 3. Get Log Statistics

**Endpoint**: `GET /api/logs/stats`

**Purpose**: Retrieve aggregated log statistics and metrics

**Authentication**: Required (API Key or Development bypass)

## Query Parameters Reference

### Core Parameters

| Parameter | Type | Default | Max | Description |
|-----------|------|---------|-----|-------------|
| `level` | string | null | - | Log level filter (Debug, Information, Warning, Error, Critical) |
| `from` | DateTime | 24h ago | - | Start time filter (ISO 8601 format) |
| `to` | DateTime | now | - | End time filter (ISO 8601 format) |
| `search` | string | null | - | Search term for comprehensive text search |
| `page` | int | 1 | - | Page number for pagination |
| `pageSize` | int | 50 | 1000 | Number of results per page |

### Parameter Examples

```bash
# Basic level filtering
GET /api/logs?level=Error

# Time range filtering
GET /api/logs?from=2024-01-15T10:00:00Z&to=2024-01-15T18:00:00Z

# Migration ID search
GET /api/logs?search=550e8400-e29b-41d4-a716-446655440000

# Pagination
GET /api/logs?page=2&pageSize=25

# Combined filtering
GET /api/logs?level=Error&search=categories&page=1&pageSize=10
```

## Search Patterns and Capabilities

### 1. Migration ID Search (GUID Detection)

**Auto-Detection**: When search parameter is a valid GUID, searches across:
- `migrationId` fields
- `requestId` fields  
- `entityId` fields
- `additionalData.migrationId` (nested)
- `additionalData.MigrationId` (nested)

**Example**:
```bash
GET /api/logs?search=d0fc0353-e4c7-4e08-8415-2beb6b58f58e
```

**Searches in**:
- Batch processing logs
- Individual error logs
- Migration status updates
- Entity processing records

### 2. Text Search Capabilities

**Full-Text Search** across multiple fields:
- `message` content
- `context` information
- `eventType` classifications
- `errorMessage` details
- `searchableContent` indexed fields
- `additionalData.errorMessage` (nested)
- `additionalData.searchableContent` (nested)

**Examples**:
```bash
# Entity type search
GET /api/logs?search=categories

# Error message search
GET /api/logs?search=duplicate

# Store search
GET /api/logs?search=mybigcommerce.com

# API error search
GET /api/logs?search=UnprocessableEntity
```

### 3. Log Level Filtering

**Supported Levels**:
- `Debug` - Detailed diagnostic information
- `Information` - General informational messages
- `Warning` - Warning conditions
- `Error` - Error conditions (most common for debugging)
- `Critical` - Critical error conditions

**Advanced Error Level Search**:
```bash
GET /api/logs?level=Error
```

**Searches across**:
- `Level:Error` (individual error logs)
- `level:Error` (general logs)
- `logType:MigrationError` (structured error logs)
- `category:Error` (categorized logs)
- `additionalData.logType:MigrationError` (nested)
- `additionalData.Level:Error` (nested)
- `(category:BatchProcessing AND errors:*)` (batch logs with errors)

### 4. Combined Search Patterns

**Complex Queries**:
```bash
# Find all category errors in specific migration
GET /api/logs?level=Error&search=categories+d0fc0353-e4c7-4e08-8415-2beb6b58f58e

# Find duplicate errors in last 2 hours
GET /api/logs?level=Error&search=duplicate&from=2024-01-15T16:00:00Z

# Find all errors with pagination
GET /api/logs?level=Error&page=1&pageSize=100
```

## Advanced Filtering Examples

### 1. Debugging Migration Failures

**Find all errors for a specific migration**:
```bash
GET /api/logs?level=Error&search=550e8400-e29b-41d4-a716-446655440000
```

**Find specific entity type failures**:
```bash
GET /api/logs?level=Error&search=categories&from=2024-01-15T10:00:00Z
```

**Find duplicate category errors**:
```bash
GET /api/logs?level=Error&search=duplicate+category
```

### 2. Performance Monitoring

**Find slow operations**:
```bash
GET /api/logs?search=timeout&from=2024-01-15T10:00:00Z
```

**Find rate limiting issues**:
```bash
GET /api/logs?search=rate+limit&level=Warning
```

**Find API connectivity issues**:
```bash
GET /api/logs?search=UnprocessableEntity&level=Error
```

### 3. Audit and Compliance

**Find all activities by time range**:
```bash
GET /api/logs?from=2024-01-15T00:00:00Z&to=2024-01-15T23:59:59Z
```

**Find all error conditions**:
```bash
GET /api/logs?level=Error&pageSize=1000
```

**Find specific entity processing**:
```bash
GET /api/logs?search=products&from=2024-01-15T10:00:00Z
```

## Pagination and Performance

### Standard Pagination

**Format**:
```json
{
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 150,
    "totalPages": 3
  }
}
```

**Performance Guidelines**:
- **Small pages (1-50)**: < 100ms response time
- **Medium pages (51-250)**: < 500ms response time
- **Large pages (251-1000)**: < 2 seconds response time

**Optimization Tips**:
1. Use specific time ranges to reduce dataset size
2. Combine level filtering with search for better performance
3. Use GUID search for exact migration queries
4. Consider multiple smaller queries vs one large query

### Large Dataset Handling

**For large result sets**:
```bash
# Process in chunks
GET /api/logs?search=migrationId&page=1&pageSize=100
GET /api/logs?search=migrationId&page=2&pageSize=100
# ... continue until totalPages reached
```

## Error Payload Retrieval

### Individual Error Payloads

**Endpoint Pattern**:
```
GET /api/logs/payload/{migrationId}/{requestId}/{payloadType}/{entityName}
```

**Parameters**:
- `migrationId`: Migration UUID
- `requestId`: Unique request identifier
- `payloadType`: Either `request` or `response`
- `entityName`: Entity name (e.g., "All", "Best Sellers", "categories")

**Examples**:
```bash
# Get request payload for failed category
GET /api/logs/payload/d0fc0353-e4c7-4e08-8415-2beb6b58f58e/req123/request/All

# Get response payload for failed category  
GET /api/logs/payload/d0fc0353-e4c7-4e08-8415-2beb6b58f58e/req123/response/All
```

### Payload Location in Logs

**Individual error logs contain blob URLs**:
```json
{
  "requestPayloadBlobUrl": "https://storage.blob.core.windows.net/migration-payloads/d0fc0353.../req123_request_All.gz",
  "responsePayloadBlobUrl": "https://storage.blob.core.windows.net/migration-payloads/d0fc0353.../req123_response_All.gz"
}
```

## Response Format Reference

### Standard Log Response

```json
{
  "timestamp": "2025-01-15T12:30:00Z",
  "filters": {
    "level": "Error",
    "from": "2025-01-14T12:30:00Z",
    "to": "2025-01-15T12:30:00Z",
    "search": "categories"
  },
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 25,
    "totalPages": 1
  },
  "logs": [
    {
      "migrationId": "550e8400-e29b-41d4-a716-446655440000",
      "entityType": "categories",
      "level": "Error",
      "logType": "MigrationError",
      "timestamp": "2025-01-15T12:00:00Z",
      "requestPayloadBlobUrl": "https://...",
      "responsePayloadBlobUrl": "https://...",
      "errorMessage": "API request failed with status UnprocessableEntity",
      "entityName": "All",
      "entityId": "2765",
      "httpStatusCode": 422,
      "searchableContent": "duplicate category All same parent"
    }
  ]
}
```

### Batch Processing Log Format

```json
{
  "migrationId": "550e8400-e29b-41d4-a716-446655440000",
  "entityType": "categories",
  "batchNumber": 1,
  "batchData": {
    "totalProcessed": 10,
    "successfulEntities": 0,
    "failedEntities": 10,
    "processingTimeMs": 0,
    "errors": [
      "Failed to process categories 2765: API request failed with status UnprocessableEntity: {...}"
    ]
  }
}
```

### Error Response Format

```json
{
  "error": {
    "code": "LOGS_ERROR",
    "message": "An error occurred while retrieving logs",
    "details": "Additional error context"
  }
}
```

## Query Performance Best Practices

### 1. Efficient Search Patterns

**✅ Good**:
```bash
# Specific time range + level
GET /api/logs?level=Error&from=2024-01-15T10:00:00Z&to=2024-01-15T11:00:00Z

# GUID search (most efficient)
GET /api/logs?search=550e8400-e29b-41d4-a716-446655440000

# Combined specific filters
GET /api/logs?level=Error&search=categories&pageSize=25
```

**❌ Less Efficient**:
```bash
# Very broad time range
GET /api/logs?from=2024-01-01T00:00:00Z&to=2024-12-31T23:59:59Z

# Very large page sizes
GET /api/logs?pageSize=1000

# Very vague text search
GET /api/logs?search=error
```

### 2. Recommended Query Patterns

**For debugging specific migration**:
```bash
# Start with migration ID
GET /api/logs?search={migrationId}&level=Error

# Then narrow by entity type
GET /api/logs?search={migrationId}+categories&level=Error

# Finally get detailed payloads
GET /api/logs/payload/{migrationId}/{requestId}/response/{entityName}
```

**For system monitoring**:
```bash
# Recent errors
GET /api/logs?level=Error&from=2024-01-15T11:00:00Z&pageSize=100

# Specific error patterns
GET /api/logs?level=Error&search=duplicate&pageSize=50

# Performance issues
GET /api/logs?search=timeout&level=Warning&pageSize=25
```

## Authentication and Security

### Development Environment

**Bypass for local development**:
- Set `ASPNETCORE_ENVIRONMENT=Development`
- API key authentication is bypassed
- All query capabilities available

### Production Environment

**API Key Required**:
```bash
# Header required
X-API-Key: your-api-key-here

# Example with curl
curl -H "X-API-Key: your-api-key" "http://localhost:7071/api/logs?level=Error"
```

**Rate Limiting**:
- 100 requests per minute per API key
- Burst limit of 10 requests per second
- Automatic throttling for large queries

## Summary

The BigCommerce Migration System provides comprehensive logging query capabilities with:

✅ **Multi-pattern search** (GUID detection, text search, level filtering)
✅ **Nested field support** (additionalData.* fields)
✅ **Flexible pagination** (1-1000 items per page)
✅ **Error payload retrieval** (compressed blob storage)
✅ **Performance optimization** (efficient indexing and caching)
✅ **Real-time search** (across all log types and indices)
✅ **Development-friendly** (authentication bypass for local testing)

This system enables comprehensive debugging, monitoring, and audit capabilities for all migration operations. 