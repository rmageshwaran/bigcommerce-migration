# Product-Images Phase: API Usage Patterns

## 📋 **Overview**

This document details the API usage patterns for the Product-Images phase, including endpoint specifications, request/response formats, error handling patterns, and performance optimization strategies.

## 🔗 **API Endpoints Used**

### **1. Source Image Fetching**

#### **Endpoint**: `GET /v3/catalog/products/{product_id}/images`
**Purpose**: Fetch images for a specific product from the source store

**Parameters**:
- `limit`: Maximum 50 images per request (API limit)
- `page`: Page number for pagination (1-based)
- `include_fields`: Not used (fetch all fields)

**Example Request**:
```http
GET /v3/catalog/products/123/images?limit=50&page=1
Host: api.bigcommerce.com
X-Auth-Token: {source_store_token}
Accept: application/json
```

**Example Response**:
```json
{
  "data": [
    {
      "id": 485,
      "product_id": 123,
      "is_thumbnail": true,
      "sort_order": 1,
      "description": "Main product image",
      "image_file": "o/381/product-image__98178.png",
      "url_zoom": "https://cdn8.bigcommerce.com/s-id30h7ohwf/products/123/images/485/product-image__98178.1536854227.1280.1280.png?c=2",
      "url_standard": "https://cdn8.bigcommerce.com/s-id30h7ohwf/products/123/images/485/product-image__98178.1536854227.560.850.png?c=2",
      "url_thumbnail": "https://cdn8.bigcommerce.com/s-id30h7ohwf/products/123/images/485/product-image__98178.1536854227.330.500.png?c=2",
      "url_tiny": "https://cdn8.bigcommerce.com/s-id30h7ohwf/products/123/images/485/product-image__98178.1536854227.66.100.png?c=2",
      "date_modified": "2018-09-13T15:57:07+00:00"
    }
  ],
  "meta": {
    "pagination": {
      "total": 150,
      "count": 50,
      "per_page": 50,
      "current_page": 1,
      "total_pages": 3
    }
  }
}
```

**Pagination Pattern**:
```csharp
int page = 1;
int totalFetched = 0;
const int maxImages = 1000;

do {
    var response = await apiClient.GetAsync($"/v3/catalog/products/{productId}/images?limit=50&page={page}");
    var pageImages = ExtractImages(response);
    
    await ProcessImageChunk(pageImages);
    
    totalFetched += pageImages.Count;
    page++;
} while (totalFetched < maxImages && pageImages.Count == 50);
```

### **2. Destination Product Update**

#### **Endpoint**: `PUT /v3/catalog/products/{product_id}`
**Purpose**: Update existing product with image data in destination store

**Request Body Format**:
```json
{
  "images": [
    {
      "product_id": 456,
      "image_url": "https://store-source123.mybigcommerce.com/product_images/o/381/product-image__98178.png",
      "is_thumbnail": true,
      "sort_order": 1,
      "description": "Main product image"
    },
    {
      "product_id": 456,
      "image_url": "https://store-source123.mybigcommerce.com/product_images/o/381/product-image__98179.png",
      "is_thumbnail": false,
      "sort_order": 2,
      "description": "Secondary product image"
    }
  ]
}
```

**Example Request**:
```http
PUT /v3/catalog/products/456
Host: api.bigcommerce.com
X-Auth-Token: {destination_store_token}
Content-Type: application/json
Accept: application/json

{
  "images": [ /* up to 50 images */ ]
}
```

**Success Response**:
```json
{
  "data": {
    "id": 456,
    "name": "Sample Product",
    "images": [
      {
        "id": 789,
        "product_id": 456,
        "image_url": "https://store-source123.mybigcommerce.com/product_images/o/381/product-image__98178.png",
        "is_thumbnail": true,
        "sort_order": 1,
        "description": "Main product image"
      }
    ]
  }
}
```

---

## 🔄 **Request/Response Patterns**

### **1. Streaming Fetch Pattern**

**Implementation**:
```csharp
public async Task<int> FetchProductImagesStreamingAsync(
    string sourceProductId, 
    StoreConfiguration sourceStore,
    string migrationId,
    Func<List<Dictionary<string, object>>, Task> onImageChunkFetched,
    CancellationToken cancellationToken = default)
{
    int page = 1;
    int totalFetched = 0;
    const int limit = 50;
    const int maxImages = 1000;

    while (totalFetched < maxImages)
    {
        // Fetch page of images
        var pageImages = await FetchProductImagesPageAsync(
            sourceProductId, sourceStore, page, limit, migrationId, cancellationToken);

        if (!pageImages.Any()) break;

        // Process immediately (streaming)
        await onImageChunkFetched(pageImages);

        totalFetched += pageImages.Count;
        page++;

        // Stop if we got less than limit (last page)
        if (pageImages.Count < limit) break;
    }

    return totalFetched;
}
```

**Benefits**:
- Memory efficient: Only 50 images in memory at a time
- Timeout safe: Can process chunks independently
- Cancellation friendly: Check cancellation between chunks

### **2. Chunked Update Pattern**

**Implementation**:
```csharp
public async Task<Dictionary<string, object>> ProcessProductImagesAsync(
    string productId, 
    List<Dictionary<string, object>> allImages)
{
    const int chunkSize = 50;
    var chunks = allImages.ChunkBy(chunkSize);
    
    foreach (var chunk in chunks)
    {
        var updatePayload = new Dictionary<string, object>
        {
            ["images"] = chunk.Select(img => TransformImage(img, productId)).ToList()
        };

        await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(
            new ApiRequest
            {
                Method = HttpMethod.Put,
                Endpoint = $"/v3/catalog/products/{productId}",
                Body = JsonSerializer.Serialize(updatePayload)
            });

        // Rate limiting delay
        await Task.Delay(300); // 300ms between chunks
    }
}
```

---

## 🔀 **URL Construction Patterns**

### **Virtual Path URL Construction**

**Purpose**: Use store virtual paths instead of CDN URLs for reliability

**Pattern**:
```csharp
public static string ConstructVirtualPathImageUrl(string sourceStoreId, string imageFile)
{
    // Virtual path format for reliability
    return $"https://store-{sourceStoreId}.mybigcommerce.com/product_images/{imageFile}";
}
```

**Examples**:
```csharp
// Source image_file: "o/381/product-image__98178.png"
// Source store ID: "id30h7ohwf"
// Result: "https://store-id30h7ohwf.mybigcommerce.com/product_images/o/381/product-image__98178.png"
```

**Benefits**:
- **Reliability**: Virtual paths don't depend on CDN cache status
- **Consistency**: Uniform URL format across all images
- **Compatibility**: Works with BigCommerce's internal image handling

### **Field Mapping Pattern**

**Source to Destination Mapping**:
```csharp
public Dictionary<string, object> TransformImage(Dictionary<string, object> sourceImage, string productId)
{
    return new Dictionary<string, object>
    {
        // Required fields only
        ["product_id"] = productId,
        ["image_url"] = ConstructVirtualPathImageUrl(
            _sourceStore.StoreId, 
            sourceImage["image_file"].ToString()),
        ["is_thumbnail"] = sourceImage.GetValueOrDefault("is_thumbnail", false),
        ["sort_order"] = sourceImage.GetValueOrDefault("sort_order", 0),
        ["description"] = sourceImage.GetValueOrDefault("description", "")
    };
    
    // Exclude: id, product_id (source), date_created, date_modified, alt_text, url_*
}
```

---

## ⚠️ **Error Handling Patterns**

### **1. API Rate Limiting**

**Pattern**: Exponential backoff with fixed delays
```csharp
public async Task<T> ExecuteWithRateLimit<T>(Func<Task<T>> apiCall)
{
    int retryCount = 0;
    const int maxRetries = 3;
    
    while (retryCount < maxRetries)
    {
        try
        {
            return await apiCall();
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("429"))
        {
            retryCount++;
            var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount) * 5); // 5, 10, 20 seconds
            await Task.Delay(delay);
        }
    }
    
    throw new Exception($"Failed after {maxRetries} retries due to rate limiting");
}
```

### **2. Individual Image Failure**

**Pattern**: Continue-on-error with detailed logging
```csharp
public async Task<List<Dictionary<string, object>>> ProcessImageChunks(
    List<List<Dictionary<string, object>>> imageChunks)
{
    var results = new List<Dictionary<string, object>>();
    int successCount = 0;
    int failureCount = 0;

    foreach (var chunk in imageChunks)
    {
        try
        {
            var result = await UpdateProductWithImages(chunk);
            results.Add(result);
            successCount += chunk.Count;
        }
        catch (Exception ex)
        {
            // Log error but continue processing
            await _errorHandlingService.LogStructuredMigrationErrorAsync(
                ex, CreateErrorContext(chunk), "image-chunk-update", 
                _migrationId, "product-images", productId);
            
            failureCount += chunk.Count;
            
            // Continue with next chunk
            continue;
        }
    }

    _logger.LogInformation("Processed {ProductId}: {SuccessCount} images succeeded, {FailureCount} failed", 
        productId, successCount, failureCount);
    
    return results;
}
```

### **3. Product Skip Pattern**

**Pattern**: Graceful skipping with status tracking
```csharp
public async Task<Dictionary<string, object>> ProcessProduct(Dictionary<string, object> product)
{
    var productId = product["destination_id"].ToString();
    
    // Fetch images with streaming
    var totalImages = await _fetchService.FetchProductImagesStreamingAsync(
        product["source_id"].ToString(), _sourceStore, _migrationId,
        async (imageChunk) => {
            if (imageChunk?.Any() == true)
            {
                _hasImages = true;
                _allImageChunks.Add(imageChunk);
            }
        });

    // Skip if no images
    if (totalImages == 0 || !_hasImages)
    {
        return new Dictionary<string, object>
        {
            ["id"] = productId,
            ["status"] = "skipped",
            ["reason"] = "no_images_found",
            ["image_count"] = 0
        };
    }

    // Process images...
}
```

---

## 📊 **Performance Optimization Patterns**

### **1. Concurrent Processing**

**Pattern**: Limited concurrency with SubBatchProcessor
```csharp
// Configuration
{
  "subBatchSize": 1,        // Process one product at a time
  "maxConcurrency": 5,      // Up to 5 products simultaneously
  "processSubBatchesSequentially": false
}

// Implementation
await _subBatchProcessor.ProcessInSubBatchesAsync(
    products, 
    config, 
    async (productBatch, ct) => {
        // Each batch contains 1 product (subBatchSize: 1)
        return await ProcessSingleProduct(productBatch.First(), ct);
    },
    "product-images",
    migrationId);
```

### **2. Memory Management**

**Pattern**: Immediate processing without accumulation
```csharp
// BAD: Accumulates all images in memory
var allImages = new List<Dictionary<string, object>>();
await fetchService.FetchAllImages(productId, images => allImages.AddRange(images));
await ProcessAllImages(allImages); // 1000 images in memory

// GOOD: Process immediately
await fetchService.FetchProductImagesStreamingAsync(productId, 
    async (imageChunk) => {
        // Process 50 images immediately
        await ProcessImageChunk(imageChunk);
        // Images are garbage collected after processing
    });
```

### **3. Timeout Management**

**Pattern**: Chunked processing with progress tracking
```csharp
public async Task<ProcessingResult> ProcessProductWithTimeoutSafety(string productId)
{
    var startTime = DateTime.UtcNow;
    var chunkResults = new List<ChunkResult>();
    
    await _fetchService.FetchProductImagesStreamingAsync(productId,
        async (imageChunk) => {
            // Check timeout before each chunk
            var elapsed = DateTime.UtcNow - startTime;
            if (elapsed.TotalSeconds > 100) // Safety margin before 120s limit
            {
                _logger.LogWarning("Approaching timeout limit, stopping processing");
                return;
            }

            var chunkResult = await ProcessImageChunk(imageChunk);
            chunkResults.Add(chunkResult);
        });

    return new ProcessingResult
    {
        ProductId = productId,
        ChunksProcessed = chunkResults.Count,
        TotalImages = chunkResults.Sum(c => c.ImageCount),
        ProcessingTime = DateTime.UtcNow - startTime
    };
}
```

---

## 🔧 **Configuration Patterns**

### **Environment-Specific Settings**

**Development**:
```json
{
  "product-images": {
    "imageChunkSize": 10,        // Smaller chunks for testing
    "imageChunkDelayMs": 100,    // Faster processing
    "maxImagesPerProduct": 100   // Limited for testing
  }
}
```

**Production**:
```json
{
  "product-images": {
    "imageChunkSize": 50,        // Optimal chunk size
    "imageChunkDelayMs": 300,    // Rate limiting
    "maxImagesPerProduct": 1000  // Full capacity
  }
}
```

### **Performance Tuning Patterns**

**High-Volume Stores**:
```json
{
  "maxConcurrency": 3,         // Reduced concurrency
  "imageChunkDelayMs": 500,    // More conservative delays
  "subBatchDelayMs": 1000      // Additional delays between products
}
```

**Low-Volume Stores**:
```json
{
  "maxConcurrency": 8,         // Higher concurrency
  "imageChunkDelayMs": 200,    // Faster processing
  "subBatchDelayMs": 0         // No additional delays
}
```

---

## 📈 **Monitoring Patterns**

### **Request Logging Pattern**
```csharp
public async Task<T> LoggedApiRequest<T>(Func<Task<T>> apiCall, string operation)
{
    var stopwatch = Stopwatch.StartNew();
    try
    {
        var result = await apiCall();
        _logger.LogInformation("{Operation} completed in {Duration}ms", 
            operation, stopwatch.ElapsedMilliseconds);
        return result;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "{Operation} failed after {Duration}ms", 
            operation, stopwatch.ElapsedMilliseconds);
        throw;
    }
}
```

### **Progress Tracking Pattern**
```csharp
public async Task TrackImageProcessingProgress(string productId, int imageCount)
{
    await _progressTracker.UpdateEntityProgressAsync(new EntityProgressUpdate
    {
        MigrationId = _migrationId,
        EntityType = "product-images",
        EntityId = productId,
        Status = EntityStatus.InProgress,
        ProcessedCount = 1,
        AdditionalData = new Dictionary<string, object>
        {
            ["image_count"] = imageCount,
            ["processed_at"] = DateTime.UtcNow
        }
    });
}
```

This comprehensive API usage documentation provides developers with the patterns and practices needed to effectively work with the Product-Images phase API integrations.
