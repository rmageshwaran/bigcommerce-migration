# Product-Images Phase: Architecture Documentation

## 📋 **Overview**

The **Product-Images Phase** (Phase 4) is a specialized migration phase that updates existing products with image data from the source BigCommerce store. Unlike other phases that create new entities, this phase modifies existing products by appending image data while preserving existing product information.

## 🏗️ **Architecture Overview**

### **Phase Position in Migration Workflow**
```
Phase 1: Products → Phase 2: Product-Related → Phase 3: Variants → Phase 4: Product-Images → Phase 5: Metafields
```

### **Key Design Principles**

1. **Individual Product Processing**: Updates one product at a time to avoid bulk API limitations
2. **Memory-Efficient Streaming**: Fetches images in 50-image chunks to prevent memory exhaustion
3. **Timeout-Safe Operations**: Designed to complete within Azure Function timeout limits
4. **Continue-on-Error Policy**: Individual image failures don't stop processing of other images
5. **Blob-Based Cancellation**: Uses ICancellationStore for coordinated cancellation across instances

---

## 🔧 **Component Architecture**

### **1. Discovery Strategy: ProductImagesDiscoveryStrategy**

**Purpose**: Identifies products that need image migration based on successfully migrated products from Phase 1.

**Key Features**:
- Queries `EntityProgress` table instead of BigCommerce API
- Returns success count as total count for efficient processing
- No actual entity discovery - uses existing product mappings

**Dependencies**:
- `IMigrationStorageService` - EntityProgress queries
- `ICancellationStore` - Blob-based cancellation

```csharp
// Discovery flow
EntityProgress products = await storageService.GetSuccessfulEntitiesAsync(migrationId, "products");
return new EntityDiscoveryResult { TotalCount = products.SuccessCount };
```

### **2. Fetch Service: ProductImagesFetchService**

**Purpose**: Streams product images from source store with memory-efficient pagination.

**Key Features**:
- Streaming fetch using callback pattern
- Maximum 1000 images per product
- 50 images per page for memory management
- Custom JSON parsing for performance

**API Endpoint**: `GET /v3/catalog/products/{product_id}/images?limit=50&page=X`

**Memory Management**:
```csharp
// Streaming approach - only one chunk in memory at a time
for (int page = 1; page <= maxPages && totalFetched < maxImages; page++)
{
    var pageImages = await FetchProductImagesPageAsync(...);
    await onImageChunkFetched(pageImages); // Process immediately
    totalFetched += pageImages.Count;
}
```

### **3. Transform Strategy: ProductImagesTransformStrategy**

**Purpose**: Converts source image data to BigCommerce product update payload format.

**Key Features**:
- Array-based transformation for bulk image updates
- Virtual path URL construction for reliability
- Field filtering to required properties only
- Skip logic for products with no images

**URL Construction**:
```csharp
// Virtual path format for reliability
string imageUrl = $"https://store-{sourceStore.StoreId}.mybigcommerce.com/product_images/{imageFile}";
```

**Output Format**:
```json
{
  "id": "product_id",
  "images": [
    {
      "product_id": "123",
      "image_url": "https://store-source123.mybigcommerce.com/product_images/image.jpg",
      "is_thumbnail": true,
      "sort_order": 1,
      "description": "Product image"
    }
  ]
}
```

### **4. Creation Strategy: ProductImagesCreationStrategy**

**Purpose**: Orchestrates the complete image migration workflow for each product.

**Key Features**:
- SubBatchProcessor integration for concurrency control
- Individual product API calls (`PUT /v3/catalog/products/{product_id}`)
- Chunked image processing (max 50 images per API call)
- Comprehensive error handling and logging

**Workflow**:
```csharp
1. SubBatchProcessor.ProcessInSubBatchesAsync() // 5 concurrent products
2. For each product:
   a. FetchProductImagesStreamingAsync() // Stream images in chunks
   b. TransformEntityAsync() // Transform each chunk
   c. PUT /v3/catalog/products/{product_id} // Update with 50 images max
   d. Repeat for remaining image chunks
3. Track success/failure counts
```

---

## ⚙️ **Configuration Architecture**

### **Phase Configuration**
```json
{
  "ParallelProcessing": {
    "subBatchConfigurations": {
      "product-images": {
        "entityType": "product-images",
        "chunkSize": 20,
        "subBatchSize": 1,
        "maxConcurrency": 5,
        "imageChunkSize": 50,
        "imageChunkDelayMs": 300,
        "maxImagesPerProduct": 1000
      }
    }
  }
}
```

### **Performance Parameters**

| Parameter | Value | Purpose |
|-----------|-------|---------|
| `chunkSize` | 20 | Products per processing chunk |
| `subBatchSize` | 1 | Individual product processing |
| `maxConcurrency` | 5 | Concurrent product processing |
| `imageChunkSize` | 50 | Images per API call |
| `imageChunkDelayMs` | 300 | Rate limiting delay |
| `maxImagesPerProduct` | 1000 | Memory protection limit |

### **Timeout Safety Calculations**
```
Worst Case Scenario (1000 images):
- Chunks per product: 20 (1000 ÷ 50)
- Delay time: 19 × 300ms = 5.7s
- API time: 20 × 2.5s = 50s
- Total per product: 55.7s
- Azure Function limit: 120s ✅
```

---

## 🔄 **Data Flow Architecture**

### **Input Data Structure**
```json
{
  "source_id": "123",
  "destination_id": "456", 
  "source_store_id": "source-store-hash"
}
```

### **Processing Flow**

1. **Discovery Phase**
   ```
   EntityProgress Query → Product IDs → EntityMappings → Processing Queue
   ```

2. **Fetch Phase** 
   ```
   Source Product ID → API Pagination → Image Chunks → Stream Processing
   ```

3. **Transform Phase**
   ```
   Image Chunks → Field Mapping → URL Construction → Product Payload
   ```

4. **Creation Phase**
   ```
   Product Payload → API Update → Success/Failure Tracking → Progress Update
   ```

### **Error Handling Flow**
```
Image Fetch Error → Log to OpenSearch → Continue with Next Product
API Update Error → Log Structured Error → Continue with Next Chunk
Transform Error → Return Null → Skip Product → Log Warning
Cancellation → Stop Processing → Return Partial Results
```

---

## 🏛️ **Integration Architecture**

### **Strategy Factory Integration**
```csharp
// Automatic discovery via dependency injection
services.AddScoped<IEntityDiscoveryStrategy, ProductImagesDiscoveryStrategy>();
services.AddScoped<IEntityTransformStrategy, ProductImagesTransformStrategy>();
services.AddScoped<IEntityCreationStrategy, ProductImagesCreationStrategy>();
```

### **Pipeline Integration**
```csharp
// ProcessEntityChunkActivity routing
case "product-images":
    return await ProcessStandardEntitiesForChunk(entities, batchRequest, chunkNumber);
```

### **Service Dependencies**
- **Core Services**: IApiRequestHandler, ICancellationStore, ISubBatchProcessor
- **Business Services**: IProductImagesFetchService, IEntityErrorHandlingService
- **Infrastructure**: ILogger, ISubBatchConfigurationService

---

## 📊 **Performance Architecture**

### **Memory Management**
- **Streaming Approach**: Only 50 images in memory at any time
- **Concurrent Limit**: Maximum 5 products × 50 images = 250KB active memory
- **Garbage Collection**: Immediate disposal after chunk processing

### **Rate Limiting**
- **API Throttling**: 300ms delay between image chunk updates
- **Concurrency Control**: Maximum 5 concurrent product updates
- **Request Spacing**: Prevents API rate limit violations

### **Scalability Design**
- **Horizontal Scaling**: Each function instance processes independently
- **Vertical Scaling**: Memory-efficient design supports larger Azure Function sizes
- **Load Distribution**: SubBatchProcessor evenly distributes work

---

## 🔐 **Security Architecture**

### **Authentication**
- **Store Access Tokens**: Secure token management via StoreConfiguration
- **API Key Validation**: Proper authentication for all BigCommerce API calls

### **Data Protection**
- **No Sensitive Data Storage**: Images processed in-memory only
- **Audit Logging**: All operations logged to OpenSearch for compliance
- **Error Sanitization**: Sensitive data removed from error logs

### **Cancellation Security**
- **Coordinated Shutdown**: Blob-based cancellation prevents data corruption
- **Graceful Termination**: In-flight operations complete before shutdown
- **State Consistency**: EntityProgress accurately reflects completion state

---

## 🔧 **Deployment Architecture**

### **Azure Function Configuration**
```json
{
  "functionTimeout": "00:10:00",
  "extensions": {
    "durableTask": {
      "hubName": "BigCommerceMigration"
    }
  }
}
```

### **Environment Variables**
- **Development**: `appsettings.Development.json`
- **Production**: `appsettings.json` + Environment variables
- **Docker**: `local.settings.docker.json` + `docker-compose.yml`

### **Monitoring Integration**
- **Application Insights**: Performance and error tracking
- **OpenSearch**: Structured logging and error aggregation
- **SignalR**: Real-time progress updates to dashboard

---

## 🎯 **Quality Assurance Architecture**

### **Testing Strategy**
- **Unit Tests**: 94 tests covering all strategies and services
- **Integration Tests**: 13 tests covering end-to-end scenarios
- **Performance Tests**: Timeout and memory validation
- **Configuration Tests**: Settings loading and parsing validation

### **Code Quality**
- **XML Documentation**: Comprehensive documentation for all public APIs
- **Code Analysis**: Clean builds with appropriate suppressions
- **Design Patterns**: Strategy pattern for extensibility

### **Operational Readiness**
- **Error Handling**: Comprehensive try-catch with structured logging
- **Progress Tracking**: Real-time progress updates via EntityProgress
- **Cancellation Support**: Graceful shutdown with ICancellationStore
- **Performance Monitoring**: Detailed timing and memory usage logging

---

## 📈 **Future Architecture Considerations**

### **Extensibility Points**
- **New Image Sources**: IProductImagesFetchService can be extended
- **Additional Transform Logic**: IEntityTransformStrategy supports customization
- **Alternative APIs**: Strategy pattern allows easy API endpoint changes

### **Performance Optimizations**
- **Batch Image APIs**: Future BigCommerce bulk image APIs
- **CDN Integration**: Direct CDN uploads for large images
- **Parallel Chunk Processing**: Concurrent chunk updates within single product

### **Scaling Improvements**
- **Dynamic Concurrency**: Adaptive concurrency based on API performance
- **Smart Retry Logic**: Exponential backoff for transient failures
- **Load Balancing**: Multiple function app instances for high-volume migrations

This architecture provides a robust, scalable, and maintainable foundation for product image migration in the BigCommerce migration system.
