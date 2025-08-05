# BigCommerce Migration System - Technical Documentation

## Overview

This documentation provides comprehensive technical diagrams and analysis of the BigCommerce Migration System, a sophisticated Azure serverless architecture designed for scalable, real-time BigCommerce store migrations with multi-storefront support.

## System Highlights

- **🚀 Performance**: 17x throughput improvement through parallel processing optimization
- **📊 Real-time**: Live progress tracking via Azure SignalR with sub-second updates
- **🔧 Scalable**: Serverless Azure Functions architecture handling millions of products
- **🛡️ Resilient**: Continue-on-error policy with comprehensive error handling
- **🔄 Multi-tenant**: Multi-storefront BigCommerce support with channel-specific processing
- **📈 Observable**: Complete monitoring via Application Insights and OpenSearch integration

## Documentation Index

### 1. [Architecture Diagram](./Architecture-Diagram.md)
**Purpose**: Complete system architecture overview  
**Shows**: All Azure services, components, layers, and their relationships  
**Key Features**:
- Multi-layered serverless architecture (Frontend → API → Orchestration → Processing → Storage)
- Real-time communication via Azure SignalR with centralized event factory
- Multiple storage patterns: Table Storage (operational), Blob Storage (large data), Queue Storage (messaging)
- External integrations: AWS OpenSearch, BigCommerce APIs
- Security and monitoring: Key Vault, Application Insights, Log Analytics

### 2. [Workflow Diagram](./Workflow-Diagram.md)
**Purpose**: Complete migration process flow from start to finish  
**Shows**: Decision points, error handling paths, parallel processing optimizations  
**Key Features**:
- Entity dependency ordering (Categories → Brands → Products → Variants → Images → Modifiers)
- Live cancellation support at multiple levels (Migration → Entity → Batch → Store)
- Continue-on-error policy with comprehensive error categorization
- Dynamic batch sizing and 17x parallel processing optimization
- Real-time progress tracking throughout the entire process

### 3. [Sequence Diagram](./Sequence-Diagram.md)
**Purpose**: Detailed interaction timeline between all system components  
**Shows**: Real-time communication patterns, parallel processing, error handling  
**Key Features**:
- Phase-based processing with detailed API interactions
- Parallel batch execution (up to 8 concurrent batches per entity type)
- Real-time SignalR events for every major operation
- Comprehensive error logging integration with OpenSearch
- User-initiated cancellation flow propagation

## System Architecture Summary

### Core Technology Stack
- **Frontend**: React Dashboard with real-time SignalR integration
- **Backend**: Azure Functions with Durable Functions orchestration
- **Storage**: Azure Table Storage (operational), Blob Storage (large data), Queue Storage (messaging)
- **Communication**: Azure SignalR Service for real-time updates
- **Monitoring**: Application Insights, Log Analytics Workspace
- **Security**: Azure Key Vault for secrets management
- **Analytics**: AWS OpenSearch for error logging and analytics
- **External APIs**: BigCommerce V2/V3 APIs for source and destination stores

### Key Architectural Patterns

#### 1. **Serverless Orchestration**
- Azure Durable Functions for long-running workflows
- Hierarchical orchestrators: Migration → Entity → Batch → Chunk
- Deterministic execution with replay safety
- Built-in timeout and error recovery

#### 2. **Parallel Processing Optimization**
- 17x performance improvement through concurrent batch processing
- Dynamic concurrency control based on system health
- Rate limit integration with BigCommerce APIs
- Progress aggregation across parallel operations

#### 3. **Real-time Communication**
- Centralized SignalR Event Factory for consistent messaging
- PascalCase to camelCase conversion for frontend compatibility
- Real-time progress updates every 250ms
- Live cancellation detection and propagation

#### 4. **Error Resilience**
- Continue-on-error policy: individual failures don't stop migration
- Error categorization: Infrastructure vs. Application logic
- Comprehensive structured logging to OpenSearch
- User-visible vs. internal error classification

#### 5. **Multi-tenant Support**
- Channel-specific processing for BigCommerce multi-storefront
- Dynamic category tree resolution
- Store-specific rate limiting and configuration
- Request-based credentials (no hardcoded configuration)

## Performance Characteristics

### Throughput Metrics
- **Base Performance**: ~700 products/hour (single-threaded)
- **Optimized Performance**: ~12,000+ products/hour (17x improvement)
- **Concurrency**: Up to 8 concurrent batches per entity type
- **Rate Limiting**: Dynamic 5-50 requests/second based on API health
- **Error Rate Target**: <5% for production workloads

### Scalability Features
- **Serverless Auto-scaling**: Automatic scaling based on load
- **Pay-per-use**: Cost-optimized pricing model
- **Queue-based Processing**: Handles traffic spikes gracefully
- **Resource Optimization**: Efficient memory and CPU utilization
- **Geographic Distribution**: Multi-region deployment capability

## Error Handling Strategy

### Error Classification
1. **Infrastructure Errors** → Stop migration immediately
   - Storage unavailable, network failures, authentication issues
   - Category: "Internal" (hidden from users)

2. **Application Logic Errors** → Log and continue
   - Data validation, mapping issues, field conflicts
   - Category: "Error" (visible to users for BigCommerce API errors only)

3. **Rate Limit Errors** → Dynamic backoff and retry
   - Temporary API throttling
   - Automatic rate adjustment

### Logging Strategy
- **Structured Data**: All errors logged with migrationId, entityType, errorType, severity
- **Complete Payloads**: Request/response data preserved for investigation
- **Search Capabilities**: Advanced filtering and querying via OpenSearch
- **Audit Trail**: Complete operation history for compliance

## Security & Compliance

### Data Protection
- **Encryption at Rest**: Azure Table Storage, Blob Storage encrypted
- **Encryption in Transit**: HTTPS/TLS 1.2+ for all communications
- **Credential Management**: Azure Key Vault for API keys and secrets
- **Network Security**: CORS configuration, firewall rules

### Access Control
- **Managed Identity**: Azure Functions use system-assigned identity
- **Least Privilege**: Minimal required permissions for each component
- **API Key Management**: Secure storage and rotation capabilities
- **Audit Logging**: Complete access and operation logs

## Monitoring & Observability

### Real-time Monitoring
- **Application Insights**: Performance metrics, error tracking, dependencies
- **Log Analytics**: Centralized logging with KQL queries
- **SignalR Events**: Real-time progress and error notifications
- **Custom Dashboards**: Migration-specific monitoring views

### Performance Metrics
- **Throughput**: Entities processed per hour
- **Error Rates**: Success/failure ratios by entity type
- **API Health**: BigCommerce API response times and error rates
- **Resource Utilization**: Function execution times and memory usage

## Getting Started

1. **Review Architecture**: Start with [Architecture Diagram](./Architecture-Diagram.md) for system overview
2. **Understand Flow**: Study [Workflow Diagram](./Workflow-Diagram.md) for process understanding
3. **Analyze Interactions**: Examine [Sequence Diagram](./Sequence-Diagram.md) for detailed interactions
4. **Setup Environment**: Follow deployment guides in main project documentation
5. **Monitor Operations**: Use provided dashboards and monitoring tools

## Contributing

When updating these diagrams:
1. Ensure Mermaid syntax is valid
2. Test rendering in GitHub or compatible viewers
3. Update corresponding documentation text
4. Maintain consistency across all three diagrams
5. Follow the established color coding and styling conventions

## Support

For questions about these diagrams or the system architecture:
- Review the source code in corresponding directories
- Check Application Insights for runtime behavior
- Examine Log Analytics for detailed operation logs
- Consult OpenSearch for error analysis and troubleshooting