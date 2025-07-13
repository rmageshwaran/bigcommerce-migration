# Logging API Quick Reference Guide

## Common Query Patterns

### 🔍 **Debug Migration Failures**
```bash
# Find all errors for specific migration
GET /api/logs?level=Error&search=d0fc0353-e4c7-4e08-8415-2beb6b58f58e

# Find category duplicate errors
GET /api/logs?level=Error&search=duplicate+category

# Find specific entity failures
GET /api/logs?level=Error&search=categories&from=2024-01-15T10:00:00Z
```

### 📊 **Monitor System Health**
```bash
# Recent errors (last hour)
GET /api/logs?level=Error&from=2024-01-15T11:00:00Z&pageSize=100

# Rate limiting issues
GET /api/logs?search=rate+limit&level=Warning

# API connectivity problems
GET /api/logs?search=UnprocessableEntity&level=Error
```

### 🕵️ **Audit and Investigation**
```bash
# All activities in date range
GET /api/logs?from=2024-01-15T00:00:00Z&to=2024-01-15T23:59:59Z

# Specific entity processing
GET /api/logs?search=products&from=2024-01-15T10:00:00Z

# User activity tracking
GET /api/logs?search=user@company.com
```

### 📄 **Get Detailed Error Payloads**
```bash
# Request payload for failed category
GET /api/logs/payload/d0fc0353-e4c7-4e08-8415-2beb6b58f58e/req123/request/All

# Response payload for failed category
GET /api/logs/payload/d0fc0353-e4c7-4e08-8415-2beb6b58f58e/req123/response/All
```

## Parameter Quick Reference

| Parameter | Example | Description |
|-----------|---------|-------------|
| `level` | `Error` | Filter by log level |
| `search` | `categories` | Search text or GUID |
| `from` | `2024-01-15T10:00:00Z` | Start time (ISO 8601) |
| `to` | `2024-01-15T18:00:00Z` | End time (ISO 8601) |
| `page` | `2` | Page number |
| `pageSize` | `100` | Results per page (max 1000) |

## Response Format

```json
{
  "timestamp": "2025-01-15T12:30:00Z",
  "filters": { "level": "Error", "search": "categories" },
  "pagination": { "page": 1, "pageSize": 50, "totalCount": 25, "totalPages": 1 },
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

## Performance Tips

### ✅ **Fast Queries**
- Use GUID search for specific migrations
- Combine level filtering with search terms
- Use specific time ranges
- Keep page sizes reasonable (≤250)

### ❌ **Slow Queries**
- Avoid very broad time ranges
- Don't use vague search terms
- Avoid very large page sizes (>500)
- Don't omit all filters

## Authentication

### Development (Local)
- Set `ASPNETCORE_ENVIRONMENT=Development`
- No API key required

### Production
- Add header: `X-API-Key: your-api-key`
- Rate limit: 100 requests/minute

## Error Codes

| Code | Description |
|------|-------------|
| `UNAUTHORIZED` | API key missing/invalid |
| `LOGS_ERROR` | General query error |
| `PAYLOAD_NOT_FOUND` | Error payload not found |
| `INVALID_PARAMETERS` | Invalid query parameters | 