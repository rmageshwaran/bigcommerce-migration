# Phase 6: Azure Durable Functions - Detailed Implementation Plan

## 📋 Overview

**Phase**: 6 - Azure Durable Functions Orchestration  
**Duration**: 3-4 weeks (15-20 working days)  
**Complexity**: **High** - Core orchestration engine  
**Prerequisites**: ✅ Phase 5 Complete (HTTP Functions with Azure Storage integration)  
**Success Criteria**: End-to-end migration workflow with fault tolerance and deterministic behavior

---

## 🎯 **Phase 6 Success Criteria**

### **Technical Requirements**
- [ ] **Complete Migration Workflow**: StartMigration → Processing → Completion
- [ ] **Fault Tolerance**: Automatic checkpointing and resume capability
- [ ] **Deterministic Behavior**: All orchestrators follow Azure constraints
- [ ] **Rate Limiting**: 12 requests/second compliance across all workers
- [ ] **Scalability**: Handle 10,000+ products in parallel batches
- [ ] **Error Handling**: Comprehensive retry logic and error recovery
- [ ] **Test Coverage**: 95%+ coverage for all orchestration components

### **Functional Requirements**
- [ ] **Entity Dependencies**: Categories → Products → Variants/Images/Modifiers
- [ ] **Channel Context**: Multi-storefront support with channel-specific operations
- [ ] **Progress Tracking**: Real-time migration status in OpenSearch
- [ ] **Cancellation Support**: Graceful cancellation at orchestration level
- [ ] **Audit Trail**: Complete migration history and decision logging

---

## 🗓️ **Sprint 1: Foundation & Core Orchestrators (Days 1-5)**

### **📋 Task 1.1: Project Structure Setup**
**Duration**: 0.5 days  
**Assignee**: Senior Developer  
**Priority**: Critical  

**Goals**:
- Create Durable Functions project structure
- Configure dependency injection for orchestration
- Set up basic testing framework

**Implementation Steps**:

1. **Create Durable Functions Project**
```bash
# Create new project
dotnet new func --name BigCommerce.Migration.Orchestration --framework net8.0
cd BigCommerce.Migration.Orchestration

# Add required packages
dotnet add package Microsoft.Azure.Functions.Worker
dotnet add package Microsoft.Azure.Functions.Worker.Extensions.DurableTask
dotnet add package Microsoft.Extensions.DependencyInjection
dotnet add package Microsoft.Extensions.Logging.Abstractions
```

2. **Project Structure**
```
BigCommerce.Migration.Orchestration/
├── Orchestrators/
│   ├── MigrationOrchestrator.cs
│   ├── CategoryMigrationOrchestrator.cs
│   ├── ProductMigrationOrchestrator.cs
│   └── EntityMigrationOrchestrator.cs
├── Activities/
│   ├── BigCommerceApiActivities.cs
│   ├── DataTransformationActivities.cs
│   ├── StorageActivities.cs
│   └── ValidationActivities.cs
├── Models/
│   ├── OrchestrationModels.cs
│   ├── ActivityModels.cs
│   └── MigrationContext.cs
├── Services/
│   ├── RateLimitService.cs
│   ├── BatchSizeCalculator.cs
│   └── ProgressTracker.cs
└── Extensions/
    └── ServiceCollectionExtensions.cs
```

3. **Dependency Injection Setup**
```csharp
// Program.cs
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // Add existing services
        services.AddBigCommerceMigrationServices(Configuration);
        
        // Add orchestration-specific services
        services.AddOrchestrationServices();
    })
    .Build();

// Extensions/ServiceCollectionExtensions.cs
public static IServiceCollection AddOrchestrationServices(this IServiceCollection services)
{
    services.AddSingleton<IRateLimitService, RateLimitService>();
    services.AddSingleton<IBatchSizeCalculator, BatchSizeCalculator>();
    services.AddSingleton<IProgressTracker, ProgressTracker>();
    
    return services;
}
```

**Success Criteria**: ✅ **COMPLETED**
- [x] Project builds successfully
- [x] Dependency injection configured
- [x] Basic test project created
- [x] Solution references updated

**Test Requirements**: ✅ **COMPLETED**
- [x] `ServiceCollectionExtensionsTests` - DI registration tests
- [x] `ProjectStructureTests` - Verify all required files exist (verified through TDD implementation)

---

### **📋 Task 1.2: Core Orchestration Models**
**Duration**: 1 day  
**Assignee**: Senior Developer  
**Priority**: Critical  
**Dependencies**: Task 1.1

**Goals**:
- Define all orchestration input/output models
- Create strongly-typed contexts for migration data
- Implement validation for orchestration requests

**Implementation**:

1. **Create Orchestration Models**
```csharp
// Models/OrchestrationModels.cs
public class MigrationOrchestrationRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public MigrationRequest OriginalRequest { get; set; } = new();
    public CategoryTreeContext CategoryTreeContext { get; set; } = new();
    public DateTime StartTime { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class EntityMigrationRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public StoreConfiguration SourceStore { get; set; } = new();
    public StoreConfiguration DestinationStore { get; set; } = new();
    public CategoryTreeContext CategoryTreeContext { get; set; } = new();
    public EntityConfiguration EntityConfig { get; set; } = new();
    public int BatchSize { get; set; } = 10;
    public int MaxRetries { get; set; } = 3;
}

public class BatchProcessingRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int BatchNumber { get; set; }
    public int TotalBatches { get; set; }
    public List<string> EntityIds { get; set; } = new();
    public StoreConfiguration SourceStore { get; set; } = new();
    public StoreConfiguration DestinationStore { get; set; } = new();
    public CategoryTreeContext CategoryTreeContext { get; set; } = new();
}

public class MigrationResult
{
    public string MigrationId { get; set; } = string.Empty;
    public MigrationStatus Status { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public Dictionary<string, EntityMigrationResult> EntityResults { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public MigrationStatistics Statistics { get; set; } = new();
}

public class EntityMigrationResult
{
    public string EntityType { get; set; } = string.Empty;
    public int TotalEntities { get; set; }
    public int ProcessedEntities { get; set; }
    public int SuccessfulEntities { get; set; }
    public int FailedEntities { get; set; }
    public List<EntityMapping> Mappings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public TimeSpan ProcessingTime { get; set; }
}
```

2. **Create Activity Models**
```csharp
// Models/ActivityModels.cs
public class BigCommerceApiRequest
{
    public string StoreId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    public string? Payload { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public int TimeoutMs { get; set; } = 30000;
}

public class EntityFetchRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public StoreConfiguration Store { get; set; } = new();
    public CategoryTreeContext? CategoryTreeContext { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 250;
}

public class EntityTransformationRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public List<Dictionary<string, object>> SourceEntities { get; set; } = new();
    public StoreConfiguration SourceStore { get; set; } = new();
    public StoreConfiguration DestinationStore { get; set; } = new();
    public CategoryTreeContext CategoryTreeContext { get; set; } = new();
    public Dictionary<string, EntityMapping> ExistingMappings { get; set; } = new();
}
```

**Success Criteria**: ✅ **COMPLETED**
- [x] All models properly defined with validation
- [x] JSON serialization/deserialization works correctly  
- [x] Models integrate with existing Core.Models
- [x] Comprehensive documentation and examples

**Test Requirements**: ✅ **COMPLETED**
- [x] `OrchestrationModelsTests` - Model validation and serialization
- [x] Additional activity models covered in main orchestration models
- [x] `ModelIntegrationTests` - Integration with Core.Models

---

### **📋 Task 1.3: Main Migration Orchestrator**
**Duration**: 2 days  
**Assignee**: Senior Developer  
**Priority**: Critical  
**Dependencies**: Task 1.2

**Goals**:
- Implement main migration orchestrator with deterministic behavior
- Create proper error handling and retry logic
- Implement migration lifecycle management

**Implementation**:

```csharp
// Orchestrators/MigrationOrchestrator.cs
[FunctionName("MigrationOrchestrator")]
public async Task<MigrationResult> RunMigrationOrchestrator(
    [OrchestrationTrigger] IDurableOrchestrationContext context)
{
    var request = context.GetInput<MigrationOrchestrationRequest>();
    var startTime = context.CurrentUtcDateTime;
    
    // ✅ Use deterministic logging
    context.SetCustomStatus($"Starting migration {request.MigrationId}");
    
    try
    {
        // Phase 1: Initialize migration
        await context.CallActivityAsync("InitializeMigration", request);
        
        // Phase 2: Validate stores and resolve dependencies
        var validationResult = await context.CallActivityAsync<ValidationResult>(
            "ValidateMigrationStores", request);
        
        if (!validationResult.IsValid)
        {
            return MigrationResult.Failed(request.MigrationId, validationResult.ErrorMessage);
        }
        
        // Phase 3: Process entities in dependency order
        var entityResults = new Dictionary<string, EntityMigrationResult>();
        
        // Categories first (if requested)
        if (request.OriginalRequest.Entities.Contains("categories"))
        {
            context.SetCustomStatus("Processing categories");
            var categoryRequest = new EntityMigrationRequest
            {
                MigrationId = request.MigrationId,
                EntityType = "categories",
                SourceStore = request.OriginalRequest.SourceStore,
                DestinationStore = request.OriginalRequest.DestinationStore,
                CategoryTreeContext = request.CategoryTreeContext
            };
            
            var categoryResult = await context.CallSubOrchestratorAsync<EntityMigrationResult>(
                "EntityMigrationOrchestrator", categoryRequest);
            
            entityResults["categories"] = categoryResult;
            
            // Update category tree context with mappings
            await context.CallActivityAsync("UpdateCategoryTreeContext", 
                new { MigrationId = request.MigrationId, CategoryMappings = categoryResult.Mappings });
        }
        
        // Products (if requested and categories completed successfully)
        if (request.OriginalRequest.Entities.Contains("products"))
        {
            context.SetCustomStatus("Processing products");
            var productRequest = new EntityMigrationRequest
            {
                MigrationId = request.MigrationId,
                EntityType = "products",
                SourceStore = request.OriginalRequest.SourceStore,
                DestinationStore = request.OriginalRequest.DestinationStore,
                CategoryTreeContext = request.CategoryTreeContext
            };
            
            var productResult = await context.CallSubOrchestratorAsync<EntityMigrationResult>(
                "EntityMigrationOrchestrator", productRequest);
            
            entityResults["products"] = productResult;
        }
        
        // Brands (can run in parallel with products)
        if (request.OriginalRequest.Entities.Contains("brands"))
        {
            context.SetCustomStatus("Processing brands");
            var brandRequest = new EntityMigrationRequest
            {
                MigrationId = request.MigrationId,
                EntityType = "brands",
                SourceStore = request.OriginalRequest.SourceStore,
                DestinationStore = request.OriginalRequest.DestinationStore,
                CategoryTreeContext = request.CategoryTreeContext
            };
            
            var brandResult = await context.CallSubOrchestratorAsync<EntityMigrationResult>(
                "EntityMigrationOrchestrator", brandRequest);
            
            entityResults["brands"] = brandResult;
        }
        
        // Phase 4: Finalize migration
        var endTime = context.CurrentUtcDateTime;
        var duration = endTime.Subtract(startTime);
        
        var finalResult = new MigrationResult
        {
            MigrationId = request.MigrationId,
            Status = DetermineOverallStatus(entityResults),
            StartTime = startTime,
            EndTime = endTime,
            Duration = duration,
            EntityResults = entityResults,
            Statistics = CalculateStatistics(entityResults)
        };
        
        // Update migration status in storage
        await context.CallActivityAsync("FinalizeMigration", finalResult);
        
        context.SetCustomStatus($"Migration completed: {finalResult.Status}");
        
        return finalResult;
    }
    catch (Exception ex)
    {
        // ✅ Deterministic error handling
        var errorResult = MigrationResult.Failed(request.MigrationId, ex.Message);
        await context.CallActivityAsync("LogMigrationError", 
            new { MigrationId = request.MigrationId, Error = ex.Message });
        
        return errorResult;
    }
}

private MigrationStatus DetermineOverallStatus(Dictionary<string, EntityMigrationResult> entityResults)
{
    if (entityResults.Values.All(r => r.FailedEntities == 0))
        return MigrationStatus.Completed;
    
    if (entityResults.Values.All(r => r.SuccessfulEntities == 0))
        return MigrationStatus.Failed;
    
    return MigrationStatus.CompletedWithErrors;
}
```

**Success Criteria**:
- [ ] Orchestrator follows all deterministic constraints
- [ ] Proper error handling and status management
- [ ] Integration with existing storage services
- [ ] Comprehensive logging and status updates

**Test Requirements**:
- [ ] `MigrationOrchestratorTests` - Unit tests for orchestration logic
- [ ] `DeterministicBehaviorTests` - Verify deterministic constraints
- [ ] `ErrorHandlingTests` - Test error scenarios and recovery

---

### **📋 Task 1.4: Entity Migration Sub-Orchestrator**
**Duration**: 1.5 days  
**Assignee**: Developer  
**Priority**: Critical  
**Dependencies**: Task 1.3

**Goals**:
- Create reusable entity migration orchestrator
- Implement batch processing with rate limiting
- Handle entity-specific processing logic

**Implementation**:

```csharp
// Orchestrators/EntityMigrationOrchestrator.cs
[FunctionName("EntityMigrationOrchestrator")]
public async Task<EntityMigrationResult> RunEntityMigrationOrchestrator(
    [OrchestrationTrigger] IDurableOrchestrationContext context)
{
    var request = context.GetInput<EntityMigrationRequest>();
    var startTime = context.CurrentUtcDateTime;
    
    context.SetCustomStatus($"Starting {request.EntityType} migration");
    
    try
    {
        // Phase 1: Discover entities to migrate
        var discoveryResult = await context.CallActivityAsync<EntityDiscoveryResult>(
            "DiscoverEntities", request);
        
        if (discoveryResult.TotalEntities == 0)
        {
            return EntityMigrationResult.NoEntitiesFound(request.EntityType);
        }
        
        // Phase 2: Calculate optimal batch size
        var batchSize = await context.CallActivityAsync<int>(
            "CalculateOptimalBatchSize", 
            new { EntityType = request.EntityType, StoreId = request.SourceStore.StoreId });
        
        // Phase 3: Create batches
        var batches = CreateBatches(discoveryResult.EntityIds, batchSize);
        context.SetCustomStatus($"Processing {batches.Count} batches of {request.EntityType}");
        
        // Phase 4: Process batches with rate limiting
        var batchResults = new List<BatchProcessingResult>();
        var concurrentBatches = GetConcurrentBatchCount(request.EntityType);
        
        for (int i = 0; i < batches.Count; i += concurrentBatches)
        {
            var currentBatch = batches.Skip(i).Take(concurrentBatches).ToList();
            var batchTasks = new List<Task<BatchProcessingResult>>();
            
            foreach (var batch in currentBatch)
            {
                var batchRequest = new BatchProcessingRequest
                {
                    MigrationId = request.MigrationId,
                    EntityType = request.EntityType,
                    BatchNumber = batch.BatchNumber,
                    TotalBatches = batches.Count,
                    EntityIds = batch.EntityIds,
                    SourceStore = request.SourceStore,
                    DestinationStore = request.DestinationStore,
                    CategoryTreeContext = request.CategoryTreeContext
                };
                
                batchTasks.Add(context.CallActivityAsync<BatchProcessingResult>(
                    "ProcessEntityBatch", batchRequest));
            }
            
            var batchGroupResults = await Task.WhenAll(batchTasks);
            batchResults.AddRange(batchGroupResults);
            
            // Rate limiting delay between batch groups
            if (i + concurrentBatches < batches.Count)
            {
                var delay = CalculateRateLimitDelay(request.EntityType);
                await context.CreateTimer(context.CurrentUtcDateTime.Add(delay), CancellationToken.None);
            }
        }
        
        // Phase 5: Aggregate results
        var endTime = context.CurrentUtcDateTime;
        var result = new EntityMigrationResult
        {
            EntityType = request.EntityType,
            TotalEntities = discoveryResult.TotalEntities,
            ProcessedEntities = batchResults.Sum(r => r.ProcessedCount),
            SuccessfulEntities = batchResults.Sum(r => r.SuccessCount),
            FailedEntities = batchResults.Sum(r => r.FailureCount),
            Mappings = batchResults.SelectMany(r => r.Mappings).ToList(),
            Errors = batchResults.SelectMany(r => r.Errors).ToList(),
            ProcessingTime = endTime.Subtract(startTime)
        };
        
        // Update progress tracking
        await context.CallActivityAsync("UpdateEntityProgress", result);
        
        context.SetCustomStatus($"Completed {request.EntityType}: {result.SuccessfulEntities}/{result.TotalEntities}");
        
        return result;
    }
    catch (Exception ex)
    {
        return EntityMigrationResult.Failed(request.EntityType, ex.Message);
    }
}

private List<EntityBatch> CreateBatches(List<string> entityIds, int batchSize)
{
    var batches = new List<EntityBatch>();
    for (int i = 0; i < entityIds.Count; i += batchSize)
    {
        batches.Add(new EntityBatch
        {
            BatchNumber = (i / batchSize) + 1,
            EntityIds = entityIds.Skip(i).Take(batchSize).ToList()
        });
    }
    return batches;
}

private int GetConcurrentBatchCount(string entityType)
{
    // Adjust concurrency based on entity complexity
    return entityType switch
    {
        "categories" => 3,
        "products" => 2,
        "brands" => 4,
        _ => 2
    };
}

private TimeSpan CalculateRateLimitDelay(string entityType)
{
    // Calculate delay to maintain 12 requests/second overall
    return entityType switch
    {
        "categories" => TimeSpan.FromMilliseconds(250),
        "products" => TimeSpan.FromMilliseconds(500),
        "brands" => TimeSpan.FromMilliseconds(200),
        _ => TimeSpan.FromMilliseconds(300)
    };
}
```

**Success Criteria**:
- [ ] Reusable across all entity types
- [ ] Proper batch processing with rate limiting
- [ ] Comprehensive error handling and progress tracking
- [ ] Integration with rate limiting service

**Test Requirements**:
- [ ] `EntityMigrationOrchestratorTests` - Core orchestration logic
- [ ] `BatchProcessingTests` - Batch creation and processing
- [ ] `RateLimitingTests` - Rate limiting compliance

---

## 🗓️ **Sprint 2: Activity Functions & BigCommerce Integration (Days 6-10)**

### **📋 Task 2.1: BigCommerce API Activity Functions**
**Duration**: 2 days  
**Assignee**: Senior Developer  
**Priority**: Critical  
**Dependencies**: Task 1.4

**Goals**:
- Implement all BigCommerce API activity functions
- Handle rate limiting and error recovery
- Create comprehensive API response handling

**Implementation**:

```csharp
// Activities/BigCommerceApiActivities.cs
[FunctionName("DiscoverEntities")]
public async Task<EntityDiscoveryResult> DiscoverEntities(
    [ActivityTrigger] EntityMigrationRequest request,
    ILogger log)
{
    try
    {
        log.LogInformation("Discovering {EntityType} entities for migration {MigrationId}", 
            request.EntityType, request.MigrationId);
        
        var entityIds = new List<string>();
        var page = 1;
        var pageSize = 250;
        var hasMore = true;
        
        while (hasMore)
        {
            var fetchRequest = new EntityFetchRequest
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                Store = request.SourceStore,
                CategoryTreeContext = request.CategoryTreeContext,
                Page = page,
                PageSize = pageSize
            };
            
            var pageResult = await FetchEntityPage(fetchRequest, log);
            
            if (pageResult.IsSuccess)
            {
                entityIds.AddRange(pageResult.EntityIds);
                hasMore = pageResult.HasMore;
                page++;
                
                // Rate limiting compliance
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
            else
            {
                log.LogError("Failed to fetch {EntityType} page {Page}: {Error}", 
                    request.EntityType, page, pageResult.ErrorMessage);
                break;
            }
        }
        
        return new EntityDiscoveryResult
        {
            EntityType = request.EntityType,
            TotalEntities = entityIds.Count,
            EntityIds = entityIds,
            IsSuccess = true
        };
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Entity discovery failed for {EntityType} in migration {MigrationId}", 
            request.EntityType, request.MigrationId);
        
        return EntityDiscoveryResult.Failed(request.EntityType, ex.Message);
    }
}

[FunctionName("FetchEntityPage")]
public async Task<EntityPageResult> FetchEntityPage(
    [ActivityTrigger] EntityFetchRequest request,
    ILogger log)
{
    try
    {
        // Build BigCommerce API endpoint
        var endpoint = BuildEntityEndpoint(request.EntityType, request.CategoryTreeContext);
        
        var apiRequest = new BigCommerceApiRequest
        {
            StoreId = request.Store.StoreId,
            AccessToken = request.Store.AccessToken,
            Endpoint = $"{endpoint}?page={request.Page}&limit={request.PageSize}",
            Method = "GET"
        };
        
        var response = await CallBigCommerceAPI(apiRequest, log);
        
        if (response.IsSuccess)
        {
            var entities = ParseApiResponse(response.Content, request.EntityType);
            
            return new EntityPageResult
            {
                EntityIds = entities.Select(e => e.Id).ToList(),
                HasMore = entities.Count == request.PageSize,
                IsSuccess = true
            };
        }
        
        return EntityPageResult.Failed(response.ErrorMessage);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Failed to fetch entity page: {EntityType} page {Page}", 
            request.EntityType, request.Page);
        
        return EntityPageResult.Failed(ex.Message);
    }
}

[FunctionName("ProcessEntityBatch")]
public async Task<BatchProcessingResult> ProcessEntityBatch(
    [ActivityTrigger] BatchProcessingRequest request,
    ILogger log)
{
    try
    {
        log.LogInformation("Processing batch {BatchNumber}/{TotalBatches} for {EntityType} in migration {MigrationId}", 
            request.BatchNumber, request.TotalBatches, request.EntityType, request.MigrationId);
        
        var result = new BatchProcessingResult
        {
            BatchNumber = request.BatchNumber,
            EntityType = request.EntityType,
            ProcessedCount = 0,
            SuccessCount = 0,
            FailureCount = 0,
            Mappings = new List<EntityMapping>(),
            Errors = new List<string>()
        };
        
        // Fetch source entities
        var sourceEntities = await FetchSourceEntities(request, log);
        
        // Transform entities
        var transformedEntities = await TransformEntities(sourceEntities, request, log);
        
        // Create entities in destination store
        foreach (var entity in transformedEntities)
        {
            try
            {
                var createResult = await CreateDestinationEntity(entity, request, log);
                result.ProcessedCount++;
                
                if (createResult.IsSuccess)
                {
                    result.SuccessCount++;
                    result.Mappings.Add(new EntityMapping
                    {
                        MigrationId = request.MigrationId,
                        EntityType = request.EntityType,
                        SourceId = entity.SourceId,
                        DestinationId = createResult.CreatedId,
                        SourceStoreId = request.SourceStore.StoreId,
                        DestinationStoreId = request.DestinationStore.StoreId,
                        Status = "completed",
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    result.FailureCount++;
                    result.Errors.Add($"{entity.SourceId}: {createResult.ErrorMessage}");
                }
                
                // Rate limiting between entities
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
            catch (Exception ex)
            {
                result.FailureCount++;
                result.Errors.Add($"{entity.SourceId}: {ex.Message}");
                log.LogError(ex, "Failed to process entity {EntityId} in batch {BatchNumber}", 
                    entity.SourceId, request.BatchNumber);
            }
        }
        
        // Store entity mappings
        if (result.Mappings.Any())
        {
            await StoreBatchMappings(result.Mappings, log);
        }
        
        log.LogInformation("Batch {BatchNumber} completed: {SuccessCount} successful, {FailureCount} failed", 
            request.BatchNumber, result.SuccessCount, result.FailureCount);
        
        return result;
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Batch processing failed for batch {BatchNumber} in migration {MigrationId}", 
            request.BatchNumber, request.MigrationId);
        
        return BatchProcessingResult.Failed(request.BatchNumber, ex.Message);
    }
}

private async Task<ApiResponse> CallBigCommerceAPI(BigCommerceApiRequest request, ILogger log)
{
    // Rate limiting check
    await EnsureRateLimitCompliance(request.StoreId);
    
    // Make API call with retry logic
    var retryCount = 0;
    var maxRetries = 3;
    
    while (retryCount < maxRetries)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-Auth-Token", request.AccessToken);
            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            httpClient.Timeout = TimeSpan.FromMilliseconds(request.TimeoutMs);
            
            var url = $"https://api.bigcommerce.com/stores/{request.StoreId}/v3/{request.Endpoint}";
            
            HttpResponseMessage response = request.Method.ToUpper() switch
            {
                "GET" => await httpClient.GetAsync(url),
                "POST" => await httpClient.PostAsync(url, 
                    new StringContent(request.Payload ?? "", Encoding.UTF8, "application/json")),
                "PUT" => await httpClient.PutAsync(url, 
                    new StringContent(request.Payload ?? "", Encoding.UTF8, "application/json")),
                "DELETE" => await httpClient.DeleteAsync(url),
                _ => throw new ArgumentException($"Unsupported HTTP method: {request.Method}")
            };
            
            var responseContent = await response.Content.ReadAsStringAsync();
            
            // Handle rate limiting
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                log.LogWarning("Rate limit hit, waiting {RetryAfter}ms", retryAfter.TotalMilliseconds);
                await Task.Delay(retryAfter);
                retryCount++;
                continue;
            }
            
            // Record API call for rate limiting
            await RecordApiCall(request.StoreId, response.StatusCode, response.Headers);
            
            return new ApiResponse
            {
                StatusCode = response.StatusCode,
                Content = responseContent,
                IsSuccess = response.IsSuccessStatusCode,
                Headers = response.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))
            };
        }
        catch (HttpRequestException ex)
        {
            retryCount++;
            if (retryCount >= maxRetries)
            {
                return ApiResponse.Failed($"HTTP request failed after {maxRetries} retries: {ex.Message}");
            }
            
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount))); // Exponential backoff
        }
    }
    
    return ApiResponse.Failed("Maximum retry attempts exceeded");
}
```

**Success Criteria**:
- [ ] All BigCommerce API endpoints accessible
- [ ] Rate limiting compliance (12 requests/second)
- [ ] Comprehensive error handling and retry logic
- [ ] Proper response parsing and validation

**Test Requirements**:
- [ ] `BigCommerceApiActivitiesTests` - API activity function tests
- [ ] `RateLimitingIntegrationTests` - Rate limiting compliance tests
- [ ] `ErrorHandlingTests` - API error scenario tests

---

This is the beginning of the comprehensive documentation. Would you like me to continue with the remaining tasks and sprints? I can create:

1. **Complete Sprint 2 & 3 details** with remaining activity functions
2. **Phase 7 detailed breakdown** with advanced features
3. **Phase 8 production optimization** tasks
4. **Testing strategy** for each phase
5. **Individual task templates** for developers

This approach provides:
- ✅ **Granular Tasks**: 1-2 day tasks with clear deliverables
- ✅ **Code Examples**: Actual implementation patterns
- ✅ **Test Requirements**: TDD approach maintained
- ✅ **Dependencies**: Clear task ordering
- ✅ **Success Criteria**: Measurable outcomes

Should I continue with the complete documentation set? 