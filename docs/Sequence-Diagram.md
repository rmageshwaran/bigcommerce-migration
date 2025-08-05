# BigCommerce Migration System - Sequence Diagram

## Overview

This sequence diagram shows the detailed interaction timeline between all system components during a migration, illustrating the real-time communication patterns, parallel processing, and error handling mechanisms.

## Interaction Phases

### 1. Migration Initiation Phase
- User submits migration request through React Dashboard
- Azure Functions receive and validate the request
- Migration Orchestrator initializes the migration process
- Real-time notifications sent to dashboard via SignalR

### 2. Migration Validation Phase
- Store validation against BigCommerce APIs
- Rate limit checks and configuration
- Permission and connectivity verification

### 3. Entity Processing Phase
- Entities processed in dependency order (Categories → Brands → Products → Variants → Images → Modifiers)
- Entity Discovery retrieves data from source BigCommerce store
- Parallel batch processing with up to 8 concurrent batches
- Real-time progress updates throughout the process

### 4. Parallel Batch Processing (17x Optimization)
- Multiple batches processed simultaneously
- Each batch follows: Fetch → Transform → Create → Progress Update
- Error handling and logging for each operation
- Continuous SignalR updates to dashboard

### 5. Error Handling & Logging
- Errors logged to OpenSearch with structured data
- Real-time error notifications to dashboard
- Continue-on-error policy maintains migration progress

### 6. Migration Completion
- Final statistics compilation
- Completion notification via SignalR
- Audit logs and cleanup

## Sequence Diagram

```mermaid
sequenceDiagram
    participant UI as React Dashboard
    participant AF as Azure Functions
    participant MDO as Migration Orchestrator
    participant EDO as Entity Orchestrator
    participant PPP as Parallel Pipeline
    participant EDS as Entity Discovery
    participant ETS as Entity Transform
    participant ECS as Entity Creation
    participant SREF as SignalR Factory
    participant ASR as Azure SignalR
    participant ATS as Table Storage
    participant BCAPI as BigCommerce API
    participant OSE as OpenSearch

    Note over UI,OSE: Migration Initiation Phase
    UI->>AF: POST /api/migration/start<br/>{source, destination, entities}
    AF->>MDO: Start migration orchestration
    MDO->>ATS: Initialize migration record
    MDO->>SREF: Create migration started event
    SREF->>ASR: Send "migrationProgress" event
    ASR-->>UI: Real-time migration started

    Note over UI,OSE: Migration Validation Phase  
    MDO->>AF: Validate stores activity
    AF->>BCAPI: Test source store API
    AF->>BCAPI: Test destination store API
    AF-->>MDO: Validation result
    
    Note over UI,OSE: Entity Processing Phase (Per Entity Type)
    MDO->>EDO: Process Categories<br/>(first by dependency order)
    EDO->>AF: Check rate limits
    AF-->>EDO: Rate limit status
    
    EDO->>EDS: Discover categories
    EDS->>BCAPI: GET /v3/catalog/categories<br/>?limit=250&include=...
    BCAPI-->>EDS: Categories page 1
    EDS->>BCAPI: GET /v3/catalog/categories<br/>?page=2&limit=250
    BCAPI-->>EDS: Categories page 2
    EDS-->>EDO: Discovery result<br/>{totalCount: 500, entityIds: [...]}
    
    EDO->>ATS: Start entity processing
    EDO->>SREF: Create entity started event
    SREF->>ASR: Send "entityProgress" event
    ASR-->>UI: Real-time entity started
    
    Note over UI,OSE: Parallel Batch Processing (17x Optimization)
    EDO->>PPP: Process batches in parallel<br/>Config: {maxConcurrency: 8, batchSize: 10}
    
    par Batch 1 (IDs 1-10)
        PPP->>EDS: Fetch batch entities
        EDS->>BCAPI: GET /v3/catalog/categories<br/>?id:in=1,2,3,4,5,6,7,8,9,10
        BCAPI-->>EDS: Category data
        EDS-->>PPP: Fetched entities
        
        PPP->>ETS: Transform entities
        ETS->>ETS: Apply category tree mapping
        ETS->>ETS: Remove system fields
        ETS-->>PPP: Transformed entities
        
        PPP->>ECS: Create in destination
        ECS->>BCAPI: POST /v3/catalog/categories<br/>[{transformed category data}]
        BCAPI-->>ECS: Created categories
        ECS->>ATS: Store entity mappings
        ECS-->>PPP: Creation result
        
        PPP->>SREF: Create batch progress event
        SREF->>ASR: Send "batchProgress" event
        ASR-->>UI: Real-time batch progress
    and Batch 2 (IDs 11-20)
        PPP->>EDS: Fetch batch entities
        EDS->>BCAPI: GET /v3/catalog/categories<br/>?id:in=11,12,13,14,15,16,17,18,19,20
        BCAPI-->>EDS: Category data
        EDS-->>PPP: Fetched entities
        
        PPP->>ETS: Transform entities
        ETS-->>PPP: Transformed entities
        
        PPP->>ECS: Create in destination
        ECS->>BCAPI: POST /v3/catalog/categories
        BCAPI-->>ECS: Created categories
        ECS-->>PPP: Creation result
        
        PPP->>SREF: Create batch progress event
        SREF->>ASR: Send "batchProgress" event
        ASR-->>UI: Real-time batch progress
    and Batch 3-8 (Parallel)
        Note over PPP,BCAPI: 6 more batches<br/>processing in parallel<br/>Same pattern as above
    end
    
    PPP-->>EDO: All batches complete<br/>{processed: 500, success: 495, failed: 5}
    
    Note over UI,OSE: Error Handling & Logging
    alt Batch Processing Error
        ECS->>OSE: Log error details<br/>{migrationId, entityType, errorType, payload}
        OSE-->>ECS: Error logged
        ECS->>SREF: Create error event
        SREF->>ASR: Send "errorProgress" event
        ASR-->>UI: Real-time error notification
    end
    
    Note over UI,OSE: Entity Completion
    EDO->>ATS: Update entity progress
    EDO->>SREF: Create entity completed event
    SREF->>ASR: Send "entityProgress" event
    ASR-->>UI: Real-time entity completion
    EDO-->>MDO: Entity result
    
    Note over UI,OSE: Next Entity (Brands, Products, etc.)
    MDO->>EDO: Process Brands<br/>(next in dependency order)
    Note over EDO,BCAPI: Same pattern as Categories<br/>Discovery → Parallel Processing → Completion
    
    Note over UI,OSE: Migration Completion
    MDO->>AF: Complete migration activity
    AF->>ATS: Update final migration status
    AF->>OSE: Log completion statistics
    MDO->>SREF: Create migration completed event
    SREF->>ASR: Send "migrationProgress" event
    ASR-->>UI: Real-time migration completion
    AF-->>UI: HTTP 200 Migration Complete
    
    Note over UI,OSE: Cancellation Flow (if triggered)
    alt User Cancels Migration
        UI->>AF: POST /api/migration/{id}/cancel
        AF->>ATS: Set cancellation flag
        AF->>MDO: Signal cancellation
        MDO->>EDO: Cancel entity processing
        EDO->>PPP: Cancel batch processing
        PPP->>SREF: Create cancellation event
        SREF->>ASR: Send "migrationProgress" event
        ASR-->>UI: Real-time cancellation
    end
```

## Key Interaction Patterns

### Real-time Communication
- **SignalR Events**: Every major operation generates real-time events
- **Progress Updates**: Continuous feedback to dashboard during processing
- **Error Notifications**: Immediate error alerts with detailed context
- **Cancellation Signals**: Real-time cancellation propagation through all layers

### Parallel Processing Optimization
- **Concurrent Batches**: Up to 8 batches processed simultaneously per entity type
- **Load Balancing**: Dynamic batch sizing based on performance metrics
- **Progress Aggregation**: Real-time consolidation of parallel batch progress
- **Error Isolation**: Individual batch failures don't affect other batches

### API Interaction Patterns
- **Discovery Phase**: Efficient pagination to discover all entities
- **Batch Fetching**: Optimized bulk retrieval using ID filters
- **Bulk Creation**: Efficient bulk operations for destination store
- **Rate Limit Compliance**: Automatic rate limiting and backoff

### Error Handling & Logging
- **Structured Logging**: Comprehensive error data sent to OpenSearch
- **Error Categorization**: Different handling for infrastructure vs. application errors
- **Continue-on-Error**: Migration continues despite individual entity failures
- **Audit Trail**: Complete operation history for troubleshooting

### Data Flow Patterns
- **Entity Dependencies**: Sequential processing respecting entity relationships
- **State Management**: Consistent state updates across Table Storage
- **Mapping Storage**: Entity relationship tracking between source and destination
- **Progress Tracking**: Multi-level progress (migration → entity → batch → item)

## Performance Characteristics

### Throughput Optimization
- **17x Performance Improvement**: Through parallel processing architecture
- **Dynamic Concurrency**: Adaptive concurrency based on system health
- **Rate Limit Integration**: Optimal use of BigCommerce API limits
- **Caching Strategies**: Reduced API calls through intelligent caching

### Real-time Responsiveness
- **Sub-second Updates**: Real-time progress updates every 250ms
- **Immediate Error Feedback**: Instant error notifications
- **Live Cancellation**: Real-time cancellation detection and response
- **Dashboard Synchronization**: Consistent state between backend and frontend

### Scalability Features
- **Serverless Architecture**: Automatic scaling based on load
- **Stateless Processing**: Enables horizontal scaling
- **Queue-based Communication**: Handles traffic spikes gracefully
- **Resource Optimization**: Pay-per-use model with efficient resource utilization