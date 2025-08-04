# 💻 **CODING PATTERNS QUICK REFERENCE**
## *Copy-Paste Ready Examples for Common Scenarios*

### 🎯 **PURPOSE**
Provides ready-to-use code patterns that follow established architecture. **Copy these patterns exactly**.

---

## ⚡ **SIGNALR EVENT CREATION**

### **✅ CORRECT Pattern - Using Factory**
```csharp
// Constructor injection
private readonly ISignalREventFactory _signalREventFactory;

public MyService(ISignalREventFactory signalREventFactory)
{
    _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory));
}

// Usage examples
var progressEvent = _signalREventFactory.CreateMigrationProgress(migrationId, new MigrationProgressOptions
{
    OverallProgress = 75,
    Status = "processing",
    TotalEntities = 1000,
    ProcessedEntities = 750
    // ✅ Base properties auto-populated: Timestamp, HubMethod, IsCancelled
});

var entityProgressEvent = _signalREventFactory.CreateEntityProgress(migrationId, new EntityProgressOptions
{
    EntityType = "Products",
    ProcessedCount = 250,
    TotalCount = 1000,
    CurrentBatch = 5
});

var batchProgressEvent = _signalREventFactory.CreateBatchProgress(migrationId, new BatchProgressOptions
{
    BatchNumber = 3,
    SubBatchNumber = 1,
    EntityType = "Categories",
    Progress = 0.6m
});
```

### **❌ WRONG Pattern - Manual Creation**
```csharp
// NEVER DO THIS
var progressEvent = new MigrationProgressEvent 
{
    MigrationId = migrationId,
    Timestamp = DateTime.UtcNow, // Manual population
    // Missing validation, inconsistent properties
};
```

---

## 🛡️ **ERROR HANDLING PATTERNS**

### **✅ CORRECT Pattern - Durable Functions Components**
```csharp
// Activities, Orchestrators, Pipeline Components
public async Task<BatchProcessingResult> ProcessBatchAsync(BatchProcessingRequest request)
{
    try
    {
        // Processing logic here
        return new BatchProcessingResult 
        { 
            IsSuccess = true, 
            ProcessedCount = request.BatchSize 
        };
    }
    catch (OperationCanceledException)
    {
        _logger.LogInformation("Batch processing was cancelled for migration {MigrationId}", 
            request.MigrationId);
        
        return new BatchProcessingResult
        {
            IsSuccess = false,
            ErrorMessage = "Operation was cancelled",
            ProcessedCount = 0
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Batch processing failed for migration {MigrationId}", 
            request.MigrationId);
            
        return new BatchProcessingResult
        {
            IsSuccess = false,
            ErrorMessage = ex.Message,
            ProcessedCount = 0
        };
    }
}
```

### **✅ CORRECT Pattern - Infrastructure Services**
```csharp
// Services, Repositories, API Clients
public async Task<ApiResponse<Product>> CreateProductAsync(Product product, CancellationToken cancellationToken = default)
{
    try
    {
        // API call logic
        return new ApiResponse<Product> { Success = true, Data = createdProduct };
    }
    catch (OperationCanceledException)
    {
        _logger.LogInformation("Product creation was cancelled for product {ProductId}", product.Id);
        throw; // OK to re-throw in infrastructure services
    }
    catch (HttpRequestException ex)
    {
        _logger.LogError(ex, "HTTP error creating product {ProductId}", product.Id);
        return new ApiResponse<Product> { Success = false, ErrorMessage = ex.Message };
    }
}
```

### **❌ WRONG Pattern - Throwing in Durable Functions**
```csharp
// NEVER DO THIS IN ORCHESTRATORS/ACTIVITIES
catch (OperationCanceledException)
{
    throw; // Breaks Durable Functions determinism
}
```

---

## 🔧 **DEPENDENCY INJECTION PATTERNS**

### **✅ CORRECT Pattern - Service Registration**
```csharp
// In ServiceCollectionExtensions.cs
public static IServiceCollection AddMigrationServices(this IServiceCollection services)
{
    // Core services
    services.AddScoped<ISignalREventFactory, SignalREventFactory>();
    services.AddScoped<IMigrationStorageService, MigrationStorageService>();
    services.AddScoped<IBigCommerceApiClient, SimpleOptimizedBigCommerceApiClient>();
    
    // Orchestration services
    services.AddScoped<IProgressTracker, ProgressTracker>();
    services.AddScoped<IRateLimitService, DynamicRateLimitService>();
    
    return services;
}
```

### **✅ CORRECT Pattern - Constructor Injection**
```csharp
public class ProductMigrationService
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly IProgressTracker _progressTracker;
    private readonly ISignalREventFactory _signalREventFactory;
    private readonly ILogger<ProductMigrationService> _logger;

    public ProductMigrationService(
        IBigCommerceApiClient apiClient,
        IProgressTracker progressTracker,
        ISignalREventFactory signalREventFactory,
        ILogger<ProductMigrationService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _progressTracker = progressTracker ?? throw new ArgumentNullException(nameof(progressTracker));
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}
```

---

## 🧪 **TESTING PATTERNS (TDD)**

### **✅ CORRECT Pattern - Unit Test Structure**
```csharp
[Test]
public async Task ProcessProductBatch_WithValidProducts_ProcessesSuccessfully()
{
    // Arrange
    var products = CreateTestProducts(5);
    var migrationId = Guid.NewGuid().ToString();
    
    _mockApiClient
        .Setup(x => x.CreateProductAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ApiResponse<Product> { Success = true });
        
    _mockProgressTracker
        .Setup(x => x.UpdateEntityProgressAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
        .Returns(Task.CompletedTask);

    // Act
    var result = await _productMigrationService.ProcessProductBatchAsync(
        new ProductBatchRequest { MigrationId = migrationId, Products = products });

    // Assert
    result.Should().NotBeNull();
    result.IsSuccess.Should().BeTrue();
    result.ProcessedCount.Should().Be(5);
    result.ErrorCount.Should().Be(0);
    
    _mockApiClient.Verify(
        x => x.CreateProductAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), 
        Times.Exactly(5));
}

[Test]
public async Task ProcessProductBatch_WithCancellation_StopsProcessing()
{
    // Arrange
    var products = CreateTestProducts(10);
    var migrationId = Guid.NewGuid().ToString();
    var cancellationToken = new CancellationTokenSource();
    
    _mockApiClient
        .Setup(x => x.CreateProductAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
        .Callback(() => cancellationToken.Cancel()) // Cancel after first call
        .ThrowsAsync(new OperationCanceledException());

    // Act
    var result = await _productMigrationService.ProcessProductBatchAsync(
        new ProductBatchRequest { MigrationId = migrationId, Products = products },
        cancellationToken.Token);

    // Assert
    result.Should().NotBeNull();
    result.IsSuccess.Should().BeFalse();
    result.ErrorMessage.Should().Contain("cancelled");
    result.ProcessedCount.Should().BeLessThan(10);
}
```

### **✅ CORRECT Pattern - Test Helper Methods**
```csharp
private static List<Product> CreateTestProducts(int count)
{
    return Enumerable.Range(1, count)
        .Select(i => new Product
        {
            Id = i,
            Name = $"Test Product {i}",
            Sku = $"TEST-SKU-{i:000}",
            Price = 10.99m + i
        })
        .ToList();
}

private static BatchProcessingRequest CreateBatchRequest(string migrationId, int batchSize = 10)
{
    return new BatchProcessingRequest
    {
        MigrationId = migrationId,
        BatchNumber = 1,
        BatchSize = batchSize,
        EntityType = "Products"
    };
}
```

---

## 🚀 **API INTEGRATION PATTERNS**

### **✅ CORRECT Pattern - BigCommerce API Calls**
```csharp
public async Task<ApiResponse<Product>> CreateProductWithRateLimitingAsync(Product product)
{
    // Check rate limit before API call
    await _rateLimitService.WaitForRateLimitAsync();
    
    try
    {
        // Use optimized client with connection pooling
        var response = await _apiClient.CreateProductAsync(product);
        
        // Track API call for rate limiting
        await _apiCallTrackingService.RecordApiCallAsync(
            storeHash: _configuration.DestinationStoreHash,
            endpoint: "products",
            success: response.Success);
            
        return response;
    }
    catch (HttpRequestException ex) when (ex.Message.Contains("429"))
    {
        // Handle rate limit exceeded
        _logger.LogWarning("Rate limit exceeded, waiting before retry");
        await Task.Delay(TimeSpan.FromSeconds(2));
        throw;
    }
}
```

### **✅ CORRECT Pattern - Batch API Operations**
```csharp
public async Task<List<ApiResponse<Product>>> CreateProductsBatchAsync(List<Product> products)
{
    // Use batch API when available
    if (products.Count <= 100) // BigCommerce batch limit
    {
        return await _apiClient.CreateProductsBatchAsync(products);
    }
    
    // Fall back to individual calls with controlled concurrency
    var semaphore = new SemaphoreSlim(5, 5); // Max 5 concurrent calls
    var tasks = products.Select(async product =>
    {
        await semaphore.WaitAsync();
        try
        {
            return await CreateProductWithRateLimitingAsync(product);
        }
        finally
        {
            semaphore.Release();
        }
    });
    
    return (await Task.WhenAll(tasks)).ToList();
}
```

---

## 📊 **PROGRESS TRACKING PATTERNS**

### **✅ CORRECT Pattern - Progress Updates**
```csharp
public async Task ProcessEntityBatchWithProgressAsync(EntityBatchRequest request)
{
    // Initialize batch tracking
    await _progressTracker.StartBatchAsync(
        request.MigrationId, 
        request.EntityType, 
        request.BatchNumber, 
        request.Entities.Count);

    var processedCount = 0;
    
    foreach (var entity in request.Entities)
    {
        try
        {
            // Process individual entity
            var result = await ProcessSingleEntityAsync(entity);
            
            if (result.Success)
            {
                processedCount++;
                
                // Update progress (triggers SignalR event via factory)
                await _progressTracker.UpdateEntityProgressAsync(
                    request.MigrationId,
                    request.EntityType,
                    processedCount);
            }
        }
        catch (Exception ex)
        {
            // Log error but continue processing (continue-on-error)
            await _progressTracker.RecordEntityErrorAsync(
                request.MigrationId,
                request.EntityType,
                entity.Id,
                ex.Message);
        }
    }
    
    // Complete batch tracking
    await _progressTracker.CompleteBatchAsync(
        request.MigrationId,
        request.EntityType,
        request.BatchNumber,
        processedCount);
}
```

---

## 🔍 **SEARCH AND DISCOVERY PATTERNS**

### **✅ Use These Searches Before Implementation**
```csharp
// To understand existing patterns
"How does [specific functionality] work in the migration system?"
"Where is [component name] implemented in the codebase?"
"How are [entity type] entities processed?"

// To find similar implementations
"How does SignalR event creation work in the migration system?"
"How are Durable Functions orchestrators implemented?"
"How does BigCommerce API integration work with rate limiting?"
"How is error handling implemented in orchestrators?"

// To validate patterns
"What are the testing patterns for [component type]?"
"How is dependency injection configured for [service type]?"
"What are the performance optimization patterns?"
```

---

## 🚨 **CRITICAL REMINDERS**

### **NEVER DO:**
❌ Create SignalR events manually
❌ Throw exceptions in Durable Functions
❌ Add retry logic for API calls
❌ Skip dependency injection
❌ Skip writing tests
❌ Hard-code configuration values

### **ALWAYS DO:**
✅ Use SignalREventFactory for all events
✅ Return result objects from Durable Functions
✅ Use dependency injection for all services
✅ Write tests first (TDD)
✅ Follow continue-on-error pattern
✅ Validate against performance requirements

---

**📚 REFERENCE DOCUMENTS:**
- `MASTER-ARCHITECTURE-INDEX.md` - Complete constraints
- `PRE-WORK-CHECKLIST.md` - Mandatory pre-work steps
- `SignalR-Centralization-QUICK-REFERENCE.md` - SignalR patterns
- `Testing-Strategy-Guide.md` - Testing approach

**🎯 CURRENT SYSTEM STATUS**: 85% complete, 562/562 tests passing, production-ready core components 