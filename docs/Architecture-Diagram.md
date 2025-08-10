# BigCommerce Migration System - Architecture Diagram

## Overview

This diagram illustrates the complete system architecture of the BigCommerce Migration System, showing all Azure services, components, and their relationships. The system follows a multi-layered serverless architecture with real-time communication capabilities.

## Architecture Components

### Frontend Layer
- **React Dashboard**: Real-time progress monitoring, migration management, and error monitoring

### API Gateway Layer  
- **Azure Functions**: HTTP endpoints for migration management, dashboard APIs, and query endpoints

### Orchestration Layer
- **Migration Durable Orchestrator**: Manages entity dependencies, cancellation handling, and progress coordination
- **Entity Migration Orchestrator**: Per-entity processing with parallel batching and 17x optimization
- **Process Entity Chunk Orchestrator**: Timeout-safe batches with sub-orchestration

### Processing Layer
- **Parallel Batch Processing Pipeline**: Dynamic concurrency and progress aggregation
- **Entity Discovery Service**: API pagination and data caching
- **Entity Transform Service**: Strategy pattern and field mapping
- **Entity Creation Service**: Bulk operations and error handling

### Communication Layer
- **SignalR Event Factory**: Centralized events with consistent naming
- **SignalR Message Converter**: Frontend compatibility (PascalCase to camelCase)
- **Azure SignalR Service**: Real-time updates and connection management

### Storage Layer
- **Azure Table Storage**: Migration state, progress tracking, entity mappings
- **Azure Blob Storage**: Large payloads, error data, audit logs
- **Azure Queue Storage**: Message queuing, background processing, dead letter handling

### External Services
- **OpenSearch (AWS)**: Error logging, analytics, search capabilities
- **BigCommerce API**: Source & destination with multi-storefront support and rate limiting

### Monitoring & Security
- **Azure Key Vault**: API credentials, connection strings, secrets management
- **Application Insights**: Performance monitoring, error tracking, diagnostics
- **Log Analytics Workspace**: Centralized logging and query capabilities

## Architecture Diagram

```mermaid
graph TB
    subgraph "Frontend Layer"
        UI[React Dashboard<br/>- Real-time progress<br/>- Migration management<br/>- Error monitoring]
    end

    subgraph "API Gateway Layer"
        AF[Azure Functions<br/>HTTP Endpoints<br/>- Migration management<br/>- Dashboard APIs<br/>- Query endpoints]
    end

    subgraph "Orchestration Layer"
        MDO[Migration Durable<br/>Orchestrator<br/>- Entity dependencies<br/>- Cancellation handling<br/>- Progress coordination]
        EDO[Entity Migration<br/>Orchestrator<br/>- Per-entity processing<br/>- Parallel batching<br/>- 17x optimization]
        PCO[Process Entity Chunk<br/>Orchestrator<br/>- Timeout-safe batches<br/>- Sub-orchestration]
    end

    subgraph "Processing Layer"
        PPP[Parallel Batch<br/>Processing Pipeline<br/>- Dynamic concurrency<br/>- Progress aggregation]
        EDS[Entity Discovery<br/>Service<br/>- API pagination<br/>- Data caching]
        ETS[Entity Transform<br/>Service<br/>- Strategy pattern<br/>- Field mapping]
        ECS[Entity Creation<br/>Service<br/>- Bulk operations<br/>- Error handling]
    end

    subgraph "Communication Layer"
        SREF[SignalR Event<br/>Factory<br/>- Centralized events<br/>- Consistent naming]
        SMC[SignalR Message<br/>Converter<br/>- Frontend compatibility<br/>- PascalCase to camelCase]
        ASR[Azure SignalR<br/>Service<br/>- Real-time updates<br/>- Connection management]
    end

    subgraph "Storage Layer"
        ATS[Azure Table Storage<br/>- Migration state<br/>- Progress tracking<br/>- Entity mappings]
        ABS[Azure Blob Storage<br/>- Large payloads<br/>- Error data<br/>- Audit logs]
        AQS[Azure Queue Storage<br/>- Message queuing<br/>- Background processing<br/>- Dead letter handling]
    end

    subgraph "External Services"
        OSE[OpenSearch<br/>AWS Cluster<br/>- Error logging<br/>- Analytics<br/>- Search capabilities]
        BCAPI[BigCommerce API<br/>- Source & destination<br/>- Multi-storefront<br/>- Rate limiting]
    end

    subgraph "Monitoring & Security"
        KV[Azure Key Vault<br/>- API credentials<br/>- Connection strings<br/>- Secrets management]
        AI[Application Insights<br/>- Performance monitoring<br/>- Error tracking<br/>- Diagnostics]
        LAW[Log Analytics<br/>Workspace<br/>- Centralized logging<br/>- Query capabilities]
    end

    subgraph "Infrastructure"
        RG[Azure Resource Group<br/>- Resource management<br/>- Tagging & governance]
        SA[Storage Account<br/>- Function storage<br/>- GRS replication<br/>- Versioning enabled]
    end

    %% Connections
    UI --> AF
    AF --> MDO
    MDO --> EDO
    EDO --> PPP
    PPP --> EDS
    PPP --> ETS
    PPP --> ECS
    
    PPP --> SREF
    SREF --> SMC
    SMC --> ASR
    ASR --> UI
    
    EDS --> BCAPI
    ETS --> BCAPI
    ECS --> BCAPI
    
    MDO --> ATS
    EDO --> ATS
    PPP --> ATS
    
    ECS --> ABS
    PPP --> AQS
    
    PPP --> OSE
    ECS --> OSE
    
    AF --> KV
    AF --> AI
    AF --> LAW
    
    AF --> SA
    ATS -.-> SA
    ABS -.-> SA
    AQS -.-> SA
    
    SA -.-> RG
    KV -.-> RG
    AI -.-> RG
    LAW -.-> RG

    %% Styling
    classDef frontend fill:#e1f5fe
    classDef orchestration fill:#f3e5f5
    classDef processing fill:#e8f5e8
    classDef storage fill:#fff3e0
    classDef external fill:#ffebee
    classDef monitoring fill:#f1f8e9
    classDef infrastructure fill:#fafafa

    class UI frontend
    class MDO,EDO,PCO orchestration
    class PPP,EDS,ETS,ECS processing
    class ATS,ABS,AQS storage
    class OSE,BCAPI external
    class KV,AI,LAW monitoring
    class RG,SA infrastructure
```

## Key Features

1. **Serverless Architecture**: Built on Azure Functions for automatic scaling and cost optimization
2. **Real-time Communication**: Azure SignalR provides live progress updates to the dashboard
3. **Multi-layer Design**: Clear separation of concerns across frontend, API, orchestration, processing, and storage layers
4. **External Integration**: Seamless connection to BigCommerce APIs and AWS OpenSearch
5. **Comprehensive Monitoring**: Application Insights and Log Analytics for full observability
6. **Security**: Azure Key Vault for secure credential management
7. **Scalable Storage**: Multiple Azure storage services for different data types and access patterns