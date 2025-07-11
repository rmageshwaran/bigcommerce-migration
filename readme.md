# BigCommerce Migration Solution

## Overview

A comprehensive, scalable BigCommerce migration solution built on Azure serverless architecture with **multi-storefront support**. This system provides entity-level configuration management, adaptive batch sizing, and real-time performance optimization for migrating millions of products between BigCommerce stores across multiple channels and storefronts.

## 🚀 Key Features

### Multi-Storefront Architecture
- **Channel-specific processing** for BigCommerce multi-storefront environments
- **Automatic category tree ID resolution** for channel-specific category migrations
- **Channel context preservation** throughout the migration process
- **Multi-channel rate limiting** with channel-specific optimization

### Entity-Level Configuration
- **Configurable batch sizes** per entity type (Categories, Brands, Products, Variants, Images, etc.)
- **Dynamic concurrency control** based on entity complexity
- **Priority-based processing** with dependency management
- **Adaptive batch sizing** with real-time performance optimization

### Scalable Architecture
- **Serverless design** using Azure Functions and Durable Functions
- **Parallel processing** within BigCommerce API rate limits
- **Long-running process support** with progress tracking
- **Cost-optimized** pay-per-use pricing model

### Comprehensive Monitoring & Error Handling
- **Real-time status tracking** via OpenSearch
- **Enhanced error logging** with entity ID, name, and source system ID
- **Complete payload preservation** for failed operations (request/response)
- **Migration request payload logging** with audit trail and compliance features
- **CSV error export** with detailed investigation data
- **Performance analytics** and optimization recommendations
- **Searchable error history** with advanced filtering
- **Migration history queries** by date range and migration ID
- **Request payload querying** by date range, user, configuration profile
- **Pagination support** for all endpoints (up to 500 results per page)

### Rate Limit Management
- **Intelligent rate limiting** (80% of BigCommerce limit)
- **Shared rate limiter** across all workers
- **Automatic backoff** and retry mechanisms
- **API usage optimization** based on entity complexity

### Complex Product Handling
- **Sub-orchestration pattern** for products with 500+ variants/images
- **Timeout prevention** using Azure Durable Functions
- **Chunked processing** with configurable batch sizes
- **Progress tracking** and resumable operations

### Granular Cancellation
- **Multi-level cancellation** at migration, product, component, and batch levels
- **Graceful shutdown** with current operations completion
- **State preservation** for resume capability
- **Real-time cancellation** with immediate response

## 📁 Documentation

### Architecture & Design
- **[Documentation Summary](DOCUMENTATION-SUMMARY.md)** - Complete overview of all documentation and features
- **[Architecture Documentation](Architecture-Documentation.md)** - Complete technical specifications
- **[Entity Configuration Guide](Entity-Configuration-Guide.md)** - Detailed configuration management guide
- **[PDF Export Guide](README-PDF-Export.md)** - Instructions for converting documentation to PDF

### Visual Diagrams
- **[Complete Migration Flow](diagram-1-complete-flow.mmd)** - End-to-end process visualization
- **[Component Architecture](diagram-2-component-architecture.mmd)** - System components and relationships
- **[Sequence Flow](diagram-3-sequence-flow.mmd)** - API interaction sequences
- **[Entity Configuration Flow](diagram-4-entity-configuration.mmd)** - Configuration management process
- **[Complex Product Processing](diagram-5-complex-product-flow.mmd)** - Sub-orchestration pattern for timeout prevention
- **[Cancellation Flow](diagram-6-cancellation-flow.mmd)** - Multi-level cancellation architecture

### Implementation Guides
- **[Diagram Conversion Guide](Convert-Diagrams-to-Images.md)** - Converting Mermaid diagrams to images
- **[Testing Strategy Guide](Testing-Strategy-Guide.md)** - Comprehensive testing approach for all workflows
- **[Testing Checklist](Testing-Checklist.md)** - Practical testing checklist for workflow validation

## 🛠️ Quick Start

### 1. Migration Request with Multi-Storefront Support

```bash
POST /migrations
Content-Type: application/json

{
  "sourceStoreId": "v6q95r5n91",
  "destinationStoreId": "in2msaitrc",
  "sourceChannelId": "1",
  "destinationChannelId": "1",
  "entities": [
    {
      "entityType": "Categories",
      "batchSize": 50,
      "maxConcurrency": 2,
      "priority": 1,
      "filters": {
        "status": "active"
      }
    },
    {
      "entityType": "Products",
      "batchSize": 10,
      "maxConcurrency": 4,
      "priority": 2,
      "filters": {
        "categories": [1, 2, 3],
        "minPrice": 10.00
      }
    },
    {
      "entityType": "Variants",
      "batchSize": 20,
      "maxConcurrency": 3,
      "priority": 3,
      "parentEntity": "Products"
    }
  ],
  "globalSettings": {
    "maxApiCallsPerSecond": 12,
    "enableAdaptiveBatching": true,
    "logLevel": "INFO"
  }
}
```

### 2. Monitor Migration Progress

```bash
GET /migrations/{migrationId}
```

**Response:**
```json
{
  "migrationId": "550e8400-e29b-41d4-a716-446655440000",
  "status": "InProgress",
  "progress": {
    "totalEntities": 50000,
    "processedEntities": 12500,
    "percentComplete": 25.0
  },
  "entityStatus": {
    "Categories": {"processed": 500, "total": 500, "status": "Completed"},
    "Products": {"processed": 2500, "total": 10000, "status": "InProgress"},
    "Variants": {"processed": 0, "total": 30000, "status": "Pending"}
  }
}
```

### 3. Entity Configuration Management

```bash
GET /entity-configurations
```

**Response:**
```json
{
  "entities": [
    {
      "entityType": "Products",
      "defaultBatchSize": 10,
      "minBatchSize": 5,
      "maxBatchSize": 25,
      "recommendedConcurrency": 4,
      "complexityLevel": "High",
      "averageProcessingTime": "2.1s"
    }
  ]
}
```

## 🎯 Performance Profiles

### Conservative Profile (Maximum Reliability)
```json
{
  "Categories": {"batchSize": 25, "maxConcurrency": 1},
  "Products": {"batchSize": 5, "maxConcurrency": 2},
  "Images": {"batchSize": 3, "maxConcurrency": 1}
}
```

### Balanced Profile (Recommended)
```json
{
  "Categories": {"batchSize": 50, "maxConcurrency": 2},
  "Products": {"batchSize": 10, "maxConcurrency": 4},
  "Images": {"batchSize": 5, "maxConcurrency": 2}
}
```

### Aggressive Profile (Maximum Speed)
```json
{
  "Categories": {"batchSize": 100, "maxConcurrency": 3},
  "Products": {"batchSize": 20, "maxConcurrency": 6},
  "Images": {"batchSize": 10, "maxConcurrency": 3}
}
```

## 📊 Performance Estimates

### 10,000 Products Migration (Balanced Profile)
- **Phase 1 (Dependencies):** ~1.25 minutes
- **Phase 2 (Products):** ~14 minutes
- **Phase 3 (Components):** ~5.8 hours
- **Total:** ~6.2 hours

### 10 Million Products Migration
- **Estimated time:** 5-10 days with entity-level optimization
- **Throughput:** ~1,000-2,000 products per hour
- **Automatic optimization:** 15-25% improvement with adaptive batching

## 🛑 Migration Cancellation

### Multi-Level Cancellation Support

The system provides **granular cancellation** at every processing level:

### 1. Cancel Migration Request

```bash
DELETE /migrations/{migrationId}
```

**Response:**
```json
{
  "migrationId": "550e8400-e29b-41d4-a716-446655440000",
  "status": "Cancelling",
  "message": "Migration cancellation initiated",
  "cancellationDetails": {
    "requestTime": "2024-01-15T10:30:00Z",
    "expectedCompletionTime": "2024-01-15T10:35:00Z",
    "currentPhase": "Products",
    "granularCancellation": true
  }
}
```

### 2. Cancellation Levels

**Migration Level** - Cancel entire migration
- All processing stops gracefully
- Current API calls complete
- Full state preservation

**Product Level** - Cancel specific products
- Sub-orchestrators receive cancellation signals
- In-progress components complete current batch
- Remaining components are cancelled

**Component Level** - Cancel variants, images, or modifiers
- Current batch completes gracefully
- Remaining batches are cancelled
- Progress saved for potential resume

**Batch Level** - Cancel current processing batch
- Individual API calls complete
- Remaining items in batch are cancelled
- Detailed logging of cancellation point

### 3. Cancellation Status Tracking

```bash
GET /migrations/{migrationId}/cancellation-status
```

**Response:**
```json
{
  "migrationId": "550e8400-e29b-41d4-a716-446655440000",
  "status": "Cancelled",
  "cancellationSummary": {
    "totalEntities": 10000,
    "processedEntities": 6000,
    "cancelledEntities": 4000,
    "cancellationTime": "2024-01-15T10:35:00Z",
    "entityBreakdown": {
      "Categories": {"processed": 500, "cancelled": 0},
      "Products": {"processed": 2500, "cancelled": 1500},
      "Variants": {"processed": 3000, "cancelled": 2500}
    }
  }
}
```

### 4. Resume After Cancellation

```bash
POST /migrations/{migrationId}/resume
```

**Request Body:**
```json
{
  "resumeMode": "FromLastCheckpoint",
  "skipCancelledEntities": true,
  "retryFailedEntities": true,
  "newConfiguration": {
    "entities": [
      {
        "entityType": "Products",
        "batchSize": 5,
        "maxConcurrency": 2
      }
    ]
  }
}
```

### 5. Cancellation Behavior Examples

**Scenario 1: Cancel during product processing**
```
Migration Status: Processing Product 2,500 of 10,000
Cancellation Request: 10:30:00 AM
Result:
├── Categories: 500 processed (Complete)
├── Products: 2,500 processed, 7,500 cancelled
├── Variants: 0 processed (not started)
└── Total time to cancel: ~30 seconds
```

**Scenario 2: Cancel during complex product (500+ variants)**
```
Product Status: Processing 300 of 500 variants
Cancellation Request: 2:15:00 PM
Result:
├── Main Product: Created (Complete)
├── Variants: 300 processed, 200 cancelled
├── Images: 0 processed (not started)
└── Sub-orchestration stops gracefully
```

## 🔧 Configuration Options

### Entity-Specific Settings
- **Batch Size:** Number of items processed per API call
- **Concurrency:** Number of parallel workers
- **Priority:** Processing order (1=highest, 3=lowest)
- **Filters:** Entity-specific filtering criteria
- **Parent Entity:** Dependency relationships

### Global Settings
- **API Rate Limiting:** Configurable calls per second
- **Adaptive Batching:** Automatic performance optimization
- **Error Handling:** Retry policies and failure management
- **Logging Level:** Detailed monitoring and debugging

## 🎛️ API Endpoints

### Migration Management
- `POST /migrations` - Start new migration
- `GET /migrations` - List all migrations
- `GET /migrations/{id}` - Get migration status
- `DELETE /migrations/{id}` - Cancel migration

### Configuration Management
- `GET /entity-configurations` - Get default configurations
- `PUT /entity-configurations/{entity}` - Update entity settings
- `GET /migrations/{id}/entity-configurations` - Get per-migration settings

### Data Export & Analysis
- `GET /migrations/{id}/errors` - Get detailed error reports with pagination
- `GET /migrations/{id}/errors/{errorId}/payloads` - Get failed request/response payloads
- `GET /migrations/{id}/export` - Export CSV reports (summary, detailed, errors, payloads)
- `GET /migrations/history` - Query migration history by date range and ID
- `GET /migrations/{id}/request` - Get original migration request payload
- `GET /migrations/requests` - Query migration requests by date range
- `GET /migrations/{id}/analytics` - Get performance metrics

**Enhanced Error Querying:**
```bash
# Get errors with entity details and pagination
GET /migrations/{id}/errors?page=1&size=100&entityType=Product&errorLevel=Error

# Get failed payloads for debugging
GET /migrations/{id}/errors/{errorId}/payloads

# Export all errors to CSV
GET /migrations/{id}/export?type=errors&format=csv&includePayloads=true
```

**Migration History Querying:**
```bash
# Query by date range
GET /migrations/history?startDate=2024-01-01T00:00:00Z&endDate=2024-01-31T23:59:59Z

# Query by migration ID (partial matching)
GET /migrations/history?migrationId=550e8400&page=1&size=50

# Query with multiple filters
GET /migrations/history?status=Completed&entityType=Products&sortBy=startTime&sortOrder=desc
```

**Migration Request Payload Querying:**
```bash
# Get original request payload for specific migration
GET /migrations/{migrationId}/request

# Query migration requests by date range
GET /migrations/requests?startDate=2024-01-01T00:00:00Z&endDate=2024-01-31T23:59:59Z

# Query requests by user with full payloads
GET /migrations/requests?requestedBy=user@company.com&includePayload=true

# Query failed migration requests for analysis
GET /migrations/requests?status=Failed&includePayload=true&sortBy=timestamp&sortOrder=desc

# Query requests by configuration profile
GET /migrations/requests?configurationProfile=Aggressive&page=1&size=100
```

### Cancellation & Control
- `DELETE /migrations/{id}` - Cancel migration (granular cancellation)
- `GET /migrations/{id}/cancellation-status` - Get cancellation progress
- `POST /migrations/{id}/resume` - Resume cancelled migration

## 🔍 Monitoring & Troubleshooting

### OpenSearch Indices
- **migration-status-{date}:** Real-time migration progress
- **migration-entities-{date}:** Entity-level processing details
- **migration-errors-{date}:** Error logs with full context
- **migration-audit-{date}:** Complete audit trail

### Performance Monitoring
- **Processing Time:** Average time per entity
- **Error Rates:** Success/failure ratios
- **Throughput:** Items processed per second
- **Rate Limit Usage:** API utilization metrics

### Common Issues
1. **Rate Limit Exceeded:** Reduce batch size or concurrency
2. **High Error Rates:** Check data quality and mappings
3. **Slow Processing:** Optimize batch sizes and check API performance
4. **Memory Issues:** Reduce batch sizes for large entities
5. **Complex Product Timeouts:** Use sub-orchestration pattern for products with 500+ variants/images
6. **Need to Stop Migration:** Use granular cancellation at any processing level with resume capability

## 💰 Cost Estimates

### Monthly Azure Costs (10M product migration)
- **Azure Functions:** ~$250/month
- **Azure Storage:** ~$30/month
- **Application Insights:** ~$25/month
- **Total:** ~$305/month (OpenSearch provided separately)

### Cost Optimization
- **Serverless pricing:** Pay only for actual processing time
- **Storage efficiency:** Optimized data structures
- **Monitoring costs:** Configurable logging levels
- **Scalability:** Automatic scaling based on workload

## 🏗️ Architecture Components

### Azure Services
- **Azure Functions:** HTTP triggers and orchestration
- **Azure Durable Functions:** Long-running process management
- **Azure Storage:** Queue, Table, and Blob storage
- **Azure Key Vault:** Secrets management
- **Application Insights:** Performance monitoring

### Data Storage
- **OpenSearch:** Logging, monitoring, and analytics
- **Table Storage:** Configuration and mappings
- **Queue Storage:** Message processing
- **Blob Storage:** Large file handling and CSV exports

### External Integrations
- **BigCommerce API:** Source and destination stores
- **Rate Limiting:** Intelligent API usage management
- **Error Handling:** Comprehensive failure management

## 📈 Supported Entities

| Entity Type | Batch Size | Concurrency | Priority | Complexity |
|-------------|------------|-------------|----------|------------|
| Categories  | 50         | 2           | 1        | Low        |
| Brands      | 30         | 2           | 1        | Low        |
| Products    | 10         | 4           | 2        | High       |
| Variants    | 20         | 3           | 3        | Medium     |
| Images      | 5          | 2           | 3        | High       |
| Modifiers   | 15         | 2           | 3        | Medium     |
| Options     | 25         | 3           | 3        | Low        |

## 🧪 Testing & Validation

### Comprehensive Testing Strategy

The system includes extensive testing to validate all workflows:

#### **Unit Testing (Component Level)**
- Individual function testing with mocks
- Configuration validation testing
- Rate limiting behavior verification
- Cancellation token management testing

#### **Integration Testing (Orchestration Level)**
- Sub-orchestration timeout prevention
- Complex product processing validation
- Entity configuration application testing
- Cancellation signal propagation testing

#### **End-to-End Testing (Complete Workflows)**
- Full migration scenario testing
- Granular cancellation validation
- Resume functionality verification
- Performance profile validation

#### **Performance Testing**
- Load testing with concurrent migrations
- Stress testing with large datasets
- Rate limit compliance verification
- Resource utilization monitoring

### Key Test Scenarios

**Timeout Prevention Testing:**
```csharp
// Test complex product with 500+ variants doesn't timeout
await TestComplexProduct(500 variants, 1000 images);
// Verify sub-orchestrations are used
// Validate completion without timeout errors
```

**Granular Cancellation Testing:**
```csharp
// Test cancellation at different levels
await TestCancellationAtProductLevel();
await TestCancellationAtComponentLevel(); 
await TestCancellationAtBatchLevel();
// Verify graceful shutdown and state preservation
```

**Entity Configuration Testing:**
```csharp
// Test adaptive batch sizing
await TestAdaptiveBatchOptimization();
// Test rate limit compliance
await TestRateLimitCompliance();
// Test configuration validation
await TestInvalidConfigurationHandling();
```

### Testing Tools & Frameworks

- **xUnit + FluentAssertions + Moq** for unit testing
- **Azure Functions Test Framework** for orchestration testing
- **Testcontainers.NET** for integration testing
- **NBomber** for performance testing
- **Azure DevOps Pipelines** for automated testing

For complete testing documentation, see **[Testing Strategy Guide](Testing-Strategy-Guide.md)**.

## 🚀 Getting Started

1. **Review the [Documentation Summary](DOCUMENTATION-SUMMARY.md)** for complete overview of all features
2. **Read the [Architecture Documentation](Architecture-Documentation.md)** for complete system understanding
3. **Study the [Migration Architecture and Execution Flow](Migration-Architecture-and-Execution-Flow.md)** for detailed execution patterns
4. **Review the [BigCommerce API Entity Reference](BigCommerce-API-Entity-Reference.md)** for comprehensive API understanding
5. **Study the [Entity Configuration Guide](Entity-Configuration-Guide.md)** for configuration best practices
6. **Check the [Testing Strategy Guide](Testing-Strategy-Guide.md)** for validation approaches
7. **Start with the Balanced Profile** for your first migration
8. **Monitor performance** and adjust settings based on observed metrics
9. **Use adaptive batching** for automatic optimization

## 📞 Support

For technical questions or advanced configuration scenarios:
- Review the complete documentation files
- Check the troubleshooting guides
- Consult the performance optimization recommendations
- Contact the development team for custom configurations

---

**Built with ❤️ using Azure serverless technologies for scalable BigCommerce migrations** 