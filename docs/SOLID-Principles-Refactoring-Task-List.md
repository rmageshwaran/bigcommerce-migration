# 🚀 **SOLID Principles Refactoring Task List - TDD Implementation Plan**

## 🔄 **CURRENT STATUS UPDATE - January 10, 2025**

### **📊 Phase 3 Progress Summary**
- **Phase**: Open/Closed Principle (OCP) Implementation  
- **Overall Progress**: 🎉 **100% Complete (3 of 3 major tasks completed)** 🎉
- **Status**: ✅ **PHASE 3 COMPLETE!** - All OCP Violations Eliminated

### **✅ COMPLETED TASKS**
- **✅ Task 3.1**: Entity Creation Strategy Pattern (Strategy factory + 6 implementations)
- **✅ Task 3.2**: Entity Transform Strategy Pattern (**JUST COMPLETED**)
  - ✅ Task 3.2.1: IEntityTransformStrategy interfaces created
  - ✅ Task 3.2.2: 6 transform strategy implementations (Category, Product, Brand, Variant, Image, Modifier)
  - ✅ Task 3.2.3: EntityTransformService refactored to use strategy pattern (switch statement eliminated)

### **✅ COMPLETED TASKS - PHASE 3**
- **✅ Task 3.3**: EntityFetchService Strategy Pattern (**COMPLETED**)
  - ✅ Task 3.3.1: Create IEntityFetchStrategy interfaces (completed)
  - ✅ Task 3.3.2: Create fetch strategy implementations for all entity types (completed)  
  - ✅ Task 3.3.3: Refactor EntityFetchService to use strategy pattern (completed)

### **⏳ REMAINING PHASE 2 TASKS**
- **Task 2.3.1**: Create entity discovery strategy interfaces for DiscoverEntitiesActivity (pending)
- **Task 2.3.2**: Create strategy factory for entity discovery (pending)  
- **Task 2.3.3**: Refactor DiscoverEntitiesActivity to use strategy pattern (pending)

### **🎯 Task 3.3 Completion Details**
**Date Completed**: January 10, 2025
**Implementation Approach**: TDD with comprehensive test coverage and strategy pattern
**Key Achievements**:
- ✅ **CRITICAL OCP VIOLATION ELIMINATED**: EntityFetchService switch statement removed (lines 30-37)
- ✅ Reduced EntityFetchService complexity: All fetch methods now delegate to strategy pattern
- ✅ Created 6 fetch strategy implementations with entity-specific fetching logic  
- ✅ **PHASE 3 COMPLETE**: All Open/Closed Principle violations eliminated from system
- ✅ Maintained backward compatibility: All existing methods preserved for tests/interface
- ✅ Created comprehensive TDD test suite: `EntityFetchStrategyTests.cs`

**Files Modified/Created**:
- ✅ `src/BigCommerce.Migration.Core/Interfaces/IEntityFetchStrategy.cs` (created)
- ✅ `src/BigCommerce.Migration.Core/Interfaces/IEntityFetchStrategyFactory.cs` (created)
- ✅ `src/BigCommerce.Migration.Orchestration/Strategies/CategoryFetchStrategy.cs` (created)
- ✅ `src/BigCommerce.Migration.Orchestration/Strategies/ProductFetchStrategy.cs` (created)
- ✅ `src/BigCommerce.Migration.Orchestration/Strategies/BrandFetchStrategy.cs` (created)
- ✅ `src/BigCommerce.Migration.Orchestration/Strategies/VariantFetchStrategy.cs` (created)
- ✅ `src/BigCommerce.Migration.Orchestration/Strategies/ImageFetchStrategy.cs` (created)
- ✅ `src/BigCommerce.Migration.Orchestration/Strategies/ModifierFetchStrategy.cs` (created)
- ✅ `src/BigCommerce.Migration.Orchestration/Strategies/EntityFetchStrategyFactory.cs` (created)
- ✅ `src/BigCommerce.Migration.Orchestration/Services/EntityFetchService.cs` (refactored - switch eliminated)
- ✅ `src/BigCommerce.Migration.Orchestration/Extensions/ServiceCollectionExtensions.cs` (updated DI registrations)
- ✅ `tests/BigCommerce.Migration.UnitTests/Orchestration/Services/EntityFetchStrategyTests.cs` (created)

### **🎯 Task 3.2 Completion Details**
**Date Completed**: January 10, 2025
**Implementation Approach**: TDD with comprehensive test coverage
**Key Achievements**:
- ✅ Eliminated OCP violation: EntityTransformService switch statement removed
- ✅ Reduced EntityTransformService from 498 → 88 lines (82% reduction)
- ✅ Created 6 transform strategy implementations with entity-specific logic
- ✅ Achieved Open/Closed Principle compliance for entity transformation
- ✅ Created comprehensive test suite: `EntityTransformStrategyTests.cs`

**Files Modified/Created**:
- ✅ `src/BigCommerce.Migration.Core/Interfaces/IEntityTransformStrategy.cs` (created)
- ✅ `src/BigCommerce.Migration.Core/Interfaces/IEntityTransformStrategyFactory.cs` (created)
- ✅ `src/BigCommerce.Migration.Orchestration/Strategies/CategoryTransformStrategy.cs` (created)
- ✅ `src/BigCommerce.Migration.Orchestration/Strategies/ProductTransformStrategy.cs` (created)
- ✅ `src/BigCommerce.Migration.Orchestration/Strategies/BrandTransformStrategy.cs` (created)
- ✅ `src/BigCommerce.Migration.Orchestration/Strategies/VariantTransformStrategy.cs` (created)
- ✅ `src/BigCommerce.Migration.Orchestration/Strategies/ImageTransformStrategy.cs` (created)
- ✅ `src/BigCommerce.Migration.Orchestration/Strategies/ModifierTransformStrategy.cs` (created)
- ✅ `src/BigCommerce.Migration.Orchestration/Strategies/EntityTransformStrategyFactory.cs` (created)
- ✅ `src/BigCommerce.Migration.Orchestration/Services/EntityTransformService.cs` (refactored)
- ✅ `src/BigCommerce.Migration.Orchestration/Services/IEntityTransformService.cs` (refactored)
- ✅ `tests/BigCommerce.Migration.UnitTests/Orchestration/Services/EntityTransformStrategyTests.cs` (created)
- ✅ `tests/BigCommerce.Migration.OrchestrationTests/Services/EntityTransformServiceTests.cs` (refactored)

### **🎯 Next Phase Options**
**🎉 PHASE 3 COMPLETE!** All Open/Closed Principle violations eliminated from the system.

**Priority Options for Continuation**:
**Option 1**: **Task 2.3** - Complete Phase 2 (DiscoverEntitiesActivity strategy pattern) 
  - **Estimated Effort**: 4-5 hours 
  - **Location**: DiscoverEntitiesActivity refactoring (938 lines → strategy pattern)
  - **Benefit**: Complete SRP phase, finish all strategy pattern implementations

**Option 2**: **Phase 4** - Liskov Substitution Principle (LSP) Implementation
  - **Focus**: Ensure all interfaces are properly substitutable
  - **Estimated Effort**: 2-3 weeks
  - **Benefit**: Move to next SOLID principle

**Option 3**: **Phase 5** - Interface Segregation Principle (ISP) Implementation  
  - **Focus**: Split large interfaces into focused, cohesive interfaces
  - **Estimated Effort**: 1-2 weeks
  - **Benefit**: Better interface design and testability

**Recommended**: Start with **Option 1** to complete Phase 2, achieving 100% strategy pattern coverage across all major services.

---

## 📋 **Document Overview**

**Purpose**: Comprehensive task breakdown for SOLID principles refactoring using Test-Driven Development  
**Approach**: TDD-first implementation with incremental, testable deliverables  
**Timeline**: 4-6 weeks (20-30 working days)  
**Success Criteria**: All SOLID violations addressed with 95%+ test coverage maintained

---

## 🎯 **EXECUTIVE SUMMARY**

### **Refactoring Scope**
- **4 Major Interface Segregations** (ISP violations)
- **3 Large Class Decompositions** (SRP violations)  
- **5 Strategy Pattern Implementations** (OCP improvements)
- **15+ New Test Suites** with comprehensive coverage

### **TDD Implementation Strategy**
1. **Red Phase**: Write failing tests for desired behavior
2. **Green Phase**: Implement minimal code to pass tests
3. **Refactor Phase**: Improve code quality while maintaining test coverage
4. **Integration Phase**: Ensure all components work together

---

## 📊 **PHASE BREAKDOWN & TIMELINE**

| Phase | Focus Area | Duration | Test Priority | Dependencies |
|-------|------------|----------|---------------|--------------|
| **Phase 1** | Interface Segregation (ISP) | 8-10 days | Unit Tests | None |
| **Phase 2** | Single Responsibility (SRP) | 10-12 days | Unit + Integration | Phase 1 |
| **Phase 3** | Open/Closed Principle (OCP) | 6-8 days | Unit + Behavior | Phase 2 |
| **Phase 4** | Model Organization | 4-5 days | Unit Tests | Phase 3 |
| **Phase 5** | Integration & Validation | 3-4 days | E2E Tests | All Phases |

---

## 🔥 **PHASE 1: INTERFACE SEGREGATION (ISP) - Days 1-10**

### **CRITICAL VIOLATIONS IDENTIFIED**

#### **❌ IBigCommerceApiClient (292 lines, 25+ methods)**
- Category operations (5 methods)
- Product operations (6 methods) 
- Pagination operations (8 methods)
- Health/utility operations (3 methods)

#### **❌ IMigrationStorageService (158 lines, 22 methods)**
- Migration operations (5 methods)
- Entity mapping operations (7 methods)
- API tracking operations (3 methods)
- Cancellation operations (4 methods)

### **TASK 1.1: Split IBigCommerceApiClient Interface**
**Priority**: 🔥 Critical | **Effort**: 2-3 days | **TDD Focus**: Interface contracts

#### **Task 1.1.1: Design and Test Segregated Interfaces (4-6 hours)**

**TDD Steps**:
```csharp
// RED: Write tests for new segregated interfaces
[Test]
public void ICategoryApiClient_Should_Only_Have_Category_Methods()
{
    var categoryMethods = typeof(ICategoryApiClient).GetMethods();
    Assert.That(categoryMethods.All(m => m.Name.Contains("Category")));
    Assert.That(categoryMethods.Length, Is.EqualTo(3)); // GetTrees, Get, Create
}
```

**Implementation**:
```csharp
// GREEN: Create segregated interfaces
public interface ICategoryApiClient
{
    Task<List<Dictionary<string, object>>> GetCategoryTreesAsync(StoreConfiguration storeConfig, CancellationToken cancellationToken = default);
    Task<List<Dictionary<string, object>>> GetCategoriesAsync(StoreConfiguration storeConfig, string categoryTreeId, CancellationToken cancellationToken = default);
    Task<List<Dictionary<string, object>>> CreateCategoriesAsync(StoreConfiguration storeConfig, string categoryTreeId, List<Dictionary<string, object>> categories, CancellationToken cancellationToken = default);
}

public interface IProductApiClient
{
    Task<List<Dictionary<string, object>>> GetProductsAsync(StoreConfiguration storeConfig, int page = 1, int limit = 50, CancellationToken cancellationToken = default);
    Task<List<Dictionary<string, object>>> CreateProductsAsync(StoreConfiguration storeConfig, List<Dictionary<string, object>> products, CancellationToken cancellationToken = default);
    Task<List<ProductVariantSummary>> GetProductVariantsAsync(StoreConfiguration storeConfig, int productId, CancellationToken cancellationToken);
    Task<List<ProductImageSummary>> GetProductImagesAsync(StoreConfiguration storeConfig, int productId, CancellationToken cancellationToken);
    Task<List<ProductModifierSummary>> GetProductModifiersAsync(StoreConfiguration storeConfig, int productId, CancellationToken cancellationToken);
}

public interface IPaginationApiClient
{
    Task<BigCommercePaginatedResponse<Dictionary<string, object>>> GetPaginatedEntitiesAsync(StoreConfiguration storeConfig, string entityType, BigCommercePaginationRequest paginationRequest, CancellationToken cancellationToken);
    IAsyncEnumerable<BigCommercePaginatedResponse<Dictionary<string, object>>> GetAllEntitiesPaginatedAsync(StoreConfiguration storeConfig, string entityType, BigCommercePaginationRequest paginationRequest, CancellationToken cancellationToken);
}

public interface IApiHealthClient
{
    Task<bool> IsHealthyAsync(StoreConfiguration storeConfig, CancellationToken cancellationToken = default);
    Task<BigCommerceApiVersion> DetectApiVersionAsync(StoreConfiguration storeConfig, CancellationToken cancellationToken);
}

// Composite interface for backward compatibility
public interface IBigCommerceApiClient : ICategoryApiClient, IProductApiClient, IPaginationApiClient, IApiHealthClient
{
}
```

**Files to Create**:
- `src/BigCommerce.Migration.Core/Interfaces/ICategoryApiClient.cs`
- `src/BigCommerce.Migration.Core/Interfaces/IProductApiClient.cs`
- `src/BigCommerce.Migration.Core/Interfaces/IPaginationApiClient.cs`
- `src/BigCommerce.Migration.Core/Interfaces/IApiHealthClient.cs`
- `tests/BigCommerce.Migration.UnitTests/Core/Interfaces/SegregatedApiClientInterfaceTests.cs`

#### **Task 1.1.2: Create Focused Service Implementations (6-8 hours)**

**TDD Steps**:
```csharp
// RED: Write tests for focused implementations
[Test]
public async Task CategoryApiService_Should_Handle_Only_Category_Operations()
{
    var mockHttpClient = new Mock<HttpClient>();
    var service = new CategoryApiService(mockHttpClient.Object, mockConfig, _logger);
    await service.GetCategoryTreesAsync(storeConfig);
    // Verify only category-related HTTP calls were made
}
```

**Implementation**:
```csharp
// GREEN: Create focused service implementations
public class CategoryApiService : ICategoryApiClient
{
    private readonly HttpClient _httpClient;
    private readonly BigCommerceConfiguration _config;
    private readonly ILogger<CategoryApiService> _logger;

    public CategoryApiService(HttpClient httpClient, BigCommerceConfiguration config, ILogger<CategoryApiService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<List<Dictionary<string, object>>> GetCategoryTreesAsync(StoreConfiguration storeConfig, CancellationToken cancellationToken = default)
    {
        // Implementation focused only on category trees
    }
}
```

**Files to Create**:
- `src/BigCommerce.Migration.Infrastructure/Services/CategoryApiService.cs`
- `src/BigCommerce.Migration.Infrastructure/Services/ProductApiService.cs`
- `src/BigCommerce.Migration.Infrastructure/Services/PaginationApiService.cs`
- `src/BigCommerce.Migration.Infrastructure/Services/ApiHealthService.cs`
- `tests/BigCommerce.Migration.UnitTests/Infrastructure/Services/CategoryApiServiceTests.cs`

### **TASK 1.2: Split IMigrationStorageService Interface**
**Priority**: 🔥 Critical | **Effort**: 2-3 days | **TDD Focus**: Repository pattern

#### **Task 1.2.1: Design Repository Interfaces (3-4 hours)**

**TDD Steps**:
```csharp
// RED: Write tests for repository interfaces
[Test]
public void IMigrationRepository_Should_Only_Handle_Migration_Operations()
{
    var methods = typeof(IMigrationRepository).GetMethods();
    Assert.That(methods.All(m => 
        m.Name.Contains("Migration") || 
        m.Name.Contains("Create") || 
        m.Name.Contains("Get") || 
        m.Name.Contains("Update") || 
        m.Name.Contains("Delete")));
    Assert.That(methods.Length, Is.EqualTo(5));
}
```

**Implementation**:
```csharp
// GREEN: Create repository interfaces
public interface IMigrationRepository
{
    Task<MigrationEntry> CreateAsync(MigrationEntry entry);
    Task<MigrationEntry?> GetAsync(string migrationId);
    Task<MigrationEntry> UpdateAsync(MigrationEntry entry);
    Task<MigrationListResult> GetMigrationsAsync(MigrationQueryRequest request);
    Task<bool> DeleteAsync(string migrationId);
}

public interface IEntityMappingRepository
{
    Task<EntityMapping> CreateAsync(EntityMapping mapping);
    Task<EntityMapping?> GetAsync(string migrationId, string entityType, string sourceId);
    Task<List<EntityMapping>> GetAllAsync(string migrationId, string? entityType = null);
    Task<List<EntityMapping>> CreateBatchAsync(List<EntityMapping> mappings);
    Task<EntityMapping> UpdateAsync(EntityMapping mapping);
}

public interface IApiCallTrackingRepository
{
    Task<ApiCallTracking> CreateAsync(ApiCallTracking apiCall);
    Task<ApiCallStatistics> GetStatisticsAsync(string storeId, TimeSpan timeWindow);
    Task<ApiCallListResult> GetHistoryAsync(string migrationId, ApiCallQueryRequest request);
}

public interface ICancellationTokenRepository
{
    Task<CancellationTokenEntry> CreateAsync(string migrationId, string reason);
    Task<CancellationTokenEntry?> GetAsync(string migrationId);
    Task<CancellationTokenEntry> UpdateAsync(CancellationTokenEntry token);
    Task<bool> DeleteAsync(string migrationId);
}
```

**Files to Create**:
- `src/BigCommerce.Migration.Core/Interfaces/Repositories/IMigrationRepository.cs`
- `src/BigCommerce.Migration.Core/Interfaces/Repositories/IEntityMappingRepository.cs`
- `src/BigCommerce.Migration.Core/Interfaces/Repositories/IApiCallTrackingRepository.cs`
- `src/BigCommerce.Migration.Core/Interfaces/Repositories/ICancellationTokenRepository.cs`

#### **Task 1.2.2: Implement Repository Classes (8-10 hours)**

**TDD Steps**:
```csharp
// RED: Write comprehensive repository tests
[Test]
public async Task MigrationRepository_CreateAsync_Should_Store_Migration_Successfully()
{
    var mockTableServiceClient = new Mock<TableServiceClient>();
    var repository = new MigrationRepository(mockTableServiceClient.Object, _logger);
    var migration = TestDataFactory.CreateMigrationEntry();
    
    var result = await repository.CreateAsync(migration);
    
    Assert.That(result.Id, Is.EqualTo(migration.Id));
    mockTableServiceClient.Verify(x => x.GetTableClient("migrations"), Times.Once);
}
```

**Implementation**:
```csharp
// GREEN: Implement repository classes
public class MigrationRepository : IMigrationRepository
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<MigrationRepository> _logger;
    private const string TableName = "migrations";

    public MigrationRepository(TableServiceClient tableServiceClient, ILogger<MigrationRepository> logger)
    {
        _tableServiceClient = tableServiceClient;
        _logger = logger;
    }

    public async Task<MigrationEntry> CreateAsync(MigrationEntry entry)
    {
        var tableClient = await GetTableClientAsync();
        var tableEntity = ConvertToTableEntity(entry);
        await tableClient.AddEntityAsync(tableEntity);
        return entry;
    }
}
```

**Files to Create**:
- `src/BigCommerce.Migration.Infrastructure/Repositories/MigrationRepository.cs`
- `src/BigCommerce.Migration.Infrastructure/Repositories/EntityMappingRepository.cs`
- `src/BigCommerce.Migration.Infrastructure/Repositories/ApiCallTrackingRepository.cs`
- `src/BigCommerce.Migration.Infrastructure/Repositories/CancellationTokenRepository.cs`
- `tests/BigCommerce.Migration.UnitTests/Infrastructure/Repositories/MigrationRepositoryTests.cs`

### **TASK 1.3: Update Dependency Injection Configuration**
**Priority**: 🔥 Critical | **Effort**: 1 day

#### **Task 1.3.1: Update Service Registration (3-4 hours)**

**TDD Steps**:
```csharp
// RED: Write integration tests for DI container
[Test]
public void ServiceCollection_Should_Register_All_Segregated_Interfaces()
{
    var services = new ServiceCollection();
    services.AddBigCommerceMigrationServices();
    var provider = services.BuildServiceProvider();
    
    Assert.DoesNotThrow(() => provider.GetRequiredService<ICategoryApiClient>());
    Assert.DoesNotThrow(() => provider.GetRequiredService<IProductApiClient>());
    Assert.DoesNotThrow(() => provider.GetRequiredService<IMigrationRepository>());
}
```

**Implementation**:
```csharp
// GREEN: Update service registration
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBigCommerceMigrationServices(this IServiceCollection services)
    {
        // Register segregated API clients
        services.AddScoped<ICategoryApiClient, CategoryApiService>();
        services.AddScoped<IProductApiClient, ProductApiService>();
        services.AddScoped<IPaginationApiClient, PaginationApiService>();
        services.AddScoped<IApiHealthClient, ApiHealthService>();
        
        // Register composite client for backward compatibility
        services.AddScoped<IBigCommerceApiClient, BigCommerceApiClient>();
        
        // Register repositories
        services.AddScoped<IMigrationRepository, MigrationRepository>();
        services.AddScoped<IEntityMappingRepository, EntityMappingRepository>();
        services.AddScoped<IApiCallTrackingRepository, ApiCallTrackingRepository>();
        services.AddScoped<ICancellationTokenRepository, CancellationTokenRepository>();
        
        // Register storage service with repository composition
        services.AddScoped<IMigrationStorageService, MigrationStorageService>();
        
        return services;
    }
}
```

---

## 🏗️ **PHASE 2: SINGLE RESPONSIBILITY (SRP) - Days 11-22**

### **CRITICAL VIOLATIONS IDENTIFIED**

#### **❌ MigrationHttpFunctions (1,701 lines)**
**Current Responsibilities:**
- HTTP request handling (8 different endpoints)
- Request validation and parsing
- Business logic coordination  
- Response formatting
- Error handling and logging
- OpenAPI documentation

#### **❌ BigCommerceApiClient (898 lines)**
**Current Responsibilities:**
- HTTP client management
- Request/response handling for 6+ entity types
- Rate limiting coordination
- Error handling and retry logic
- API version detection
- Pagination logic
- Authentication management

#### **❌ DiscoverEntitiesActivity (938 lines)**
**Current Responsibilities:**
- Entity discovery orchestration
- API version detection  
- V2 vs V3 API strategy selection
- Pagination logic for different API versions
- Entity data caching
- Error handling and validation

### **TASK 2.1: Refactor MigrationHttpFunctions Class**
**Priority**: 🔥 Critical | **Effort**: 3-4 days

#### **Task 2.1.1: Split into Domain-Specific Function Classes (8-10 hours)**

**TDD Steps**:
```csharp
// RED: Write tests for focused function classes
[Test]
public void MigrationManagementFunctions_Should_Only_Handle_Management_Operations()
{
    var methods = typeof(MigrationManagementFunctions).GetMethods()
        .Where(m => m.IsPublic && m.DeclaringType == typeof(MigrationManagementFunctions));
    
    var managementMethods = new[] { "StartMigration", "CancelMigration", "PauseMigration", "ResumeMigration" };
    Assert.That(methods.Select(m => m.Name), Is.SubsetOf(managementMethods));
}
```

**Implementation**:
```csharp
// GREEN: Create focused function classes
public class MigrationManagementFunctions
{
    private readonly IQueueService _queueService;
    private readonly IMigrationStorageService _migrationStorageService;
    private readonly ILogger<MigrationManagementFunctions> _logger;

    [Function("StartMigration")]
    public async Task<HttpResponseData> StartMigration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "migrations")] HttpRequestData req,
        FunctionContext context)
    {
        // Implementation focused only on starting migrations
    }

    [Function("CancelMigration")]
    public async Task<HttpResponseData> CancelMigration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "migrations/{migrationId}")] HttpRequestData req,
        FunctionContext context)
    {
        // Implementation focused only on cancelling migrations
    }
}

public class MigrationQueryFunctions
{
    [Function("GetMigration")]
    public async Task<HttpResponseData> GetMigration(...) { }

    [Function("GetMigrations")]
    public async Task<HttpResponseData> GetMigrations(...) { }
}

public class MigrationReportingFunctions
{
    [Function("GetMigrationErrors")]
    public async Task<HttpResponseData> GetMigrationErrors(...) { }

    [Function("ExportMigrationData")]
    public async Task<HttpResponseData> ExportMigrationData(...) { }
}
```

**Files to Create**:
- `src/BigCommerce.Migration.Functions/Functions/MigrationManagementFunctions.cs`
- `src/BigCommerce.Migration.Functions/Functions/MigrationQueryFunctions.cs`
- `src/BigCommerce.Migration.Functions/Functions/MigrationReportingFunctions.cs`
- `src/BigCommerce.Migration.Functions/Functions/MigrationHealthFunctions.cs`
- `tests/BigCommerce.Migration.UnitTests/Functions/MigrationManagementFunctionsTests.cs`

### **TASK 2.2: Refactor BigCommerceApiClient Class**
**Priority**: 🔥 Critical | **Effort**: 3-4 days

#### **Task 2.2.1: Create API Request Handler Service (6-8 hours)**

**TDD Steps**:
```csharp
// RED: Write tests for API request handling
[Test]
public async Task ApiRequestHandler_Should_Handle_Rate_Limiting()
{
    var handler = new ApiRequestHandler(_httpClient, _rateLimiter, _logger);
    var request = new ApiRequest { Url = "test-url", Method = HttpMethod.Get };
    
    await handler.ExecuteRequestAsync<object>(request);
    
    _rateLimiter.Verify(r => r.WaitForAvailabilityAsync(), Times.Once);
}
```

**Implementation**:
```csharp
// GREEN: Create API request handler
public interface IApiRequestHandler
{
    Task<T> ExecuteRequestAsync<T>(ApiRequest request);
    Task<string> ExecuteRequestAsync(ApiRequest request);
}

public class ApiRequestHandler : IApiRequestHandler
{
    private readonly HttpClient _httpClient;
    private readonly IRateLimitService _rateLimiter;
    private readonly ILogger<ApiRequestHandler> _logger;

    public async Task<T> ExecuteRequestAsync<T>(ApiRequest request)
    {
        await _rateLimiter.WaitForAvailabilityAsync();
        var httpRequest = CreateHttpRequest(request);
        var response = await _httpClient.SendAsync(httpRequest);
        return await ProcessResponse<T>(response);
    }
}
```

**Files to Create**:
- `src/BigCommerce.Migration.Infrastructure/Services/ApiRequestHandler.cs`
- `src/BigCommerce.Migration.Core/Interfaces/IApiRequestHandler.cs`
- `src/BigCommerce.Migration.Core/Models/ApiRequest.cs`
- `tests/BigCommerce.Migration.UnitTests/Infrastructure/Services/ApiRequestHandlerTests.cs`

#### **Task 2.2.2: Update BigCommerceApiClient to Use Delegation (6-8 hours)**

**Implementation**:
```csharp
// GREEN: Update client to use delegation
public class BigCommerceApiClient : IBigCommerceApiClient
{
    private readonly ICategoryApiClient _categoryService;
    private readonly IProductApiClient _productService;
    private readonly IPaginationApiClient _paginationService;
    private readonly IApiHealthClient _healthService;

    public Task<List<Dictionary<string, object>>> GetCategoryTreesAsync(StoreConfiguration storeConfig, CancellationToken cancellationToken = default)
        => _categoryService.GetCategoryTreesAsync(storeConfig, cancellationToken);

    public Task<List<Dictionary<string, object>>> GetCategoriesAsync(StoreConfiguration storeConfig, string categoryTreeId, CancellationToken cancellationToken = default)
        => _categoryService.GetCategoriesAsync(storeConfig, categoryTreeId, cancellationToken);
}
```

### **TASK 2.3: Refactor DiscoverEntitiesActivity Class**
**Priority**: 🔥 Critical | **Effort**: 2-3 days

#### **Task 2.3.1: Create Entity Discovery Strategy Interfaces (4-5 hours)**

**TDD Steps**:
```csharp
// RED: Write tests for strategy pattern
[Test]
public async Task V3EntityDiscoveryStrategy_Should_Use_V3_Specific_Logic()
{
    var strategy = new V3EntityDiscoveryStrategy(_apiClient, _logger);
    var request = CreateDiscoveryRequest();
    
    var result = await strategy.DiscoverAsync(request);
    
    Assert.That(result.ApiVersion, Is.EqualTo(BigCommerceApiVersion.V3));
    Assert.That(result.SkipDiscovery, Is.False);
}
```

**Implementation**:
```csharp
// GREEN: Create strategy interfaces and implementations
public interface IEntityDiscoveryStrategy
{
    BigCommerceApiVersion SupportedApiVersion { get; }
    Task<EntityDiscoveryResult> DiscoverAsync(EntityDiscoveryRequest request, CancellationToken cancellationToken = default);
}

public class V3EntityDiscoveryStrategy : IEntityDiscoveryStrategy
{
    public BigCommerceApiVersion SupportedApiVersion => BigCommerceApiVersion.V3;
    
    public async Task<EntityDiscoveryResult> DiscoverAsync(EntityDiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        // V3-specific discovery logic with full pagination
    }
}

public class V2EntityDiscoveryStrategy : IEntityDiscoveryStrategy
{
    public BigCommerceApiVersion SupportedApiVersion => BigCommerceApiVersion.V2;
    
    public async Task<EntityDiscoveryResult> DiscoverAsync(EntityDiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        // V2-specific discovery logic (skip discovery, use direct pagination)
        return new EntityDiscoveryResult
        {
            EntityType = request.EntityType,
            TotalCount = 0,
            EntityIds = new List<string>(),
            ApiVersion = BigCommerceApiVersion.V2,
            SkipDiscovery = true
        };
    }
}
```

#### **Task 2.3.2: Create Strategy Factory (3-4 hours)**

**Implementation**:
```csharp
// GREEN: Create strategy factory
public interface IEntityDiscoveryStrategyFactory
{
    Task<IEntityDiscoveryStrategy> GetStrategyAsync(StoreConfiguration storeConfig, string entityType, CancellationToken cancellationToken = default);
}

public class EntityDiscoveryStrategyFactory : IEntityDiscoveryStrategyFactory
{
    private readonly IEnumerable<IEntityDiscoveryStrategy> _strategies;
    private readonly IBigCommerceApiClient _apiClient;

    public async Task<IEntityDiscoveryStrategy> GetStrategyAsync(StoreConfiguration storeConfig, string entityType, CancellationToken cancellationToken = default)
    {
        var apiVersion = await _apiClient.DetectApiVersionAsync(storeConfig, cancellationToken);
        
        var strategy = _strategies.FirstOrDefault(s => s.SupportedApiVersion == apiVersion);
        if (strategy == null)
            throw new NotSupportedException($"No discovery strategy found for API version {apiVersion}");
            
        return strategy;
    }
}
```

#### **Task 2.3.3: Refactor DiscoverEntitiesActivity to Use Strategy (4-5 hours)**

**Implementation**:
```csharp
// GREEN: Refactor activity to use strategy
public class DiscoverEntitiesActivity
{
    private readonly IEntityDiscoveryStrategyFactory _strategyFactory;
    private readonly ILogger<DiscoverEntitiesActivity> _logger;

    [Function("DiscoverEntitiesActivity")]
    public async Task<EntityDiscoveryResult> DiscoverEntitiesAsync(
        [ActivityTrigger] EntityDiscoveryRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var strategy = await _strategyFactory.GetStrategyAsync(request.SourceStore, request.EntityType, cancellationToken);
            var result = await strategy.DiscoverAsync(request, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover entities for migration {MigrationId}, Entity Type: {EntityType}",
                request.MigrationId, request.EntityType);
            
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0,
                EntityIds = new List<string>(),
                Errors = new List<string> { ex.Message }
            };
        }
    }
}
```

**Files to Create**:
- `src/BigCommerce.Migration.Orchestration/Services/IEntityDiscoveryStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Services/V3EntityDiscoveryStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Services/V2EntityDiscoveryStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Services/EntityDiscoveryStrategyFactory.cs`
- `tests/BigCommerce.Migration.OrchestrationTests/Services/EntityDiscoveryStrategyTests.cs`

---

## 🎨 **PHASE 3: OPEN/CLOSED PRINCIPLE (OCP) - Days 23-30**

### **TASK 3.1: Replace Entity Creation Switch Statements**
**Priority**: 🟡 Medium | **Effort**: 2-3 days

#### **Current OCP Violation**:
```csharp
// ❌ Switch statement anti-pattern
var result = request.EntityType.ToLowerInvariant() switch
{
    "categories" => await CreateCategoriesAsync(entities, request, cancellationToken),
    "products" => await CreateProductsAsync(entities, request, cancellationToken),
    "brands" => await CreateBrandsAsync(entities, request, cancellationToken),
    _ => throw new ArgumentException($"Unsupported entity type: {request.EntityType}")
};
```

#### **Task 3.1.1: Create Entity Creation Strategy Pattern (6-8 hours)**

**TDD Steps**:
```csharp
// RED: Write tests for entity creation strategies
[Test]
public void CategoryCreationStrategy_Should_Handle_Only_Categories()
{
    var strategy = new CategoryCreationStrategy(_apiClient, _logger);
    Assert.That(strategy.EntityType, Is.EqualTo("categories"));
}

[Test]
public void EntityCreateService_Should_Be_Open_For_Extension()
{
    // Test that new entity types can be added without modifying existing code
    var customStrategy = new CustomEntityCreationStrategy();
    var strategies = new[] { new CategoryCreationStrategy(), new ProductCreationStrategy(), customStrategy };
    var service = new EntityCreateService(strategies, _logger);
    
    Assert.DoesNotThrow(() => service.CreateEntitiesAsync(entities, customRequest, default));
}
```

**Implementation**:
```csharp
// GREEN: Create entity creation strategy pattern
public interface IEntityCreationStrategy
{
    string EntityType { get; }
    Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken);
}

public class CategoryCreationStrategy : IEntityCreationStrategy
{
    public string EntityType => "categories";
    
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<CategoryCreationStrategy> _logger;

    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        var storeConfig = request.DestinationStore;
        var categoryTreeId = request.CategoryTreeContext?.DestinationCategoryTreeId;

        if (string.IsNullOrWhiteSpace(categoryTreeId))
            throw new InvalidOperationException("Destination category tree ID is required for creating categories");

        return await _apiClient.CreateCategoriesAsync(storeConfig, categoryTreeId, entities, cancellationToken);
    }
}

public class ProductCreationStrategy : IEntityCreationStrategy
{
    public string EntityType => "products";
    
    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        // Product-specific creation logic
    }
}
```

#### **Task 3.1.2: Refactor EntityCreateService to Use Strategy Pattern (4-5 hours)**

**TDD Steps**:
```csharp
// RED: Write tests for strategy-based service
[Test]
public async Task EntityCreateService_Should_Select_Correct_Strategy()
{
    var categoryStrategy = new Mock<IEntityCreationStrategy>();
    categoryStrategy.Setup(s => s.EntityType).Returns("categories");
    
    var strategies = new[] { categoryStrategy.Object };
    var service = new EntityCreateService(strategies, _errorHandlingService, _logger);
    
    var request = new BatchProcessingRequest { EntityType = "categories" };
    await service.CreateEntitiesAsync(entities, request, default);
    
    categoryStrategy.Verify(s => s.CreateEntitiesAsync(entities, request, default), Times.Once);
}
```

**Implementation**:
```csharp
// GREEN: Refactor service to use strategies
public class EntityCreateService : IEntityCreateService
{
    private readonly IEnumerable<IEntityCreationStrategy> _strategies;
    private readonly IEntityErrorHandlingService _errorHandlingService;
    private readonly ILogger<EntityCreateService> _logger;

    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        var strategy = _strategies.FirstOrDefault(s => 
            s.EntityType.Equals(request.EntityType, StringComparison.OrdinalIgnoreCase));

        if (strategy == null)
            throw new ArgumentException($"Unsupported entity type: {request.EntityType}");

        try
        {
            return await strategy.CreateEntitiesAsync(entities, request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create {EntityType} entities for migration {MigrationId}", 
                request.EntityType, request.MigrationId);
            return new List<Dictionary<string, object>>();
        }
    }
}
```

#### **Task 3.1.3: Create Strategy Factory and Registration (3-4 hours)**

**Implementation**:
```csharp
// Update service registration
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEntityCreationStrategies(this IServiceCollection services)
    {
        services.AddScoped<IEntityCreationStrategy, CategoryCreationStrategy>();
        services.AddScoped<IEntityCreationStrategy, ProductCreationStrategy>();
        services.AddScoped<IEntityCreationStrategy, BrandCreationStrategy>();
        services.AddScoped<IEntityCreationStrategy, VariantCreationStrategy>();
        services.AddScoped<IEntityCreationStrategy, ImageCreationStrategy>();
        services.AddScoped<IEntityCreationStrategy, ModifierCreationStrategy>();
        
        return services;
    }
}
```

**Files to Create**:
- `src/BigCommerce.Migration.Orchestration/Services/EntityCreation/IEntityCreationStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Services/EntityCreation/CategoryCreationStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Services/EntityCreation/ProductCreationStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Services/EntityCreation/BrandCreationStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Services/EntityCreation/VariantCreationStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Services/EntityCreation/ImageCreationStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Services/EntityCreation/ModifierCreationStrategy.cs`
- `tests/BigCommerce.Migration.OrchestrationTests/Services/EntityCreation/EntityCreationStrategyTests.cs`

### **TASK 3.2: Apply Strategy Pattern to Other Switch Statements**
**Priority**: 🟡 Medium | **Effort**: 1-2 days

#### **Task 3.2.1: Refactor Entity Transform Service (4-5 hours)**
**Similar implementation to entity creation strategies but for data transformation**

#### **Task 3.2.2: Refactor Entity Fetch Service (4-5 hours)**
**Similar implementation for entity fetching operations**

---

## 📂 **PHASE 4: MODEL ORGANIZATION - Days 31-35**

### **TASK 4.1: Split Large Model Files**
**Priority**: 🟢 Low | **Effort**: 2-3 days

#### **Current Issue**: StorageModels.cs (1,201 lines)
**Contains**: Migration models, entity mapping models, API tracking models, cancellation models

#### **Task 4.1.1: Split StorageModels.cs into Domain Files (6-8 hours)**

**TDD Steps**:
```csharp
// RED: Write tests for model organization
[Test]
public void MigrationModels_Should_Be_In_Separate_File()
{
    var migrationModels = Assembly.GetAssembly(typeof(MigrationEntry))
        .GetTypes()
        .Where(t => t.Name.Contains("Migration"))
        .ToList();
    
    Assert.That(migrationModels.Any(), Is.True);
}

[Test]
public void EntityMappingModels_Should_Have_Proper_Validation()
{
    var mapping = new EntityMapping 
    { 
        MigrationId = "", 
        EntityType = "", 
        SourceId = "" 
    };
    
    var validationResults = ValidateModel(mapping);
    Assert.That(validationResults.Any(v => v.ErrorMessage.Contains("required")));
}
```

**Implementation**:
```csharp
// GREEN: Split into focused model files

// Migration-related models
// File: src/BigCommerce.Migration.Core/Models/Migration/MigrationModels.cs
public class MigrationEntry
{
    [Required]
    public string Id { get; set; } = string.Empty;
    
    [Required]
    public string SourceStoreId { get; set; } = string.Empty;
    
    // ... other migration properties
}

// Entity mapping models  
// File: src/BigCommerce.Migration.Core/Models/EntityMapping/EntityMappingModels.cs
public class EntityMapping
{
    [Required]
    public string MigrationId { get; set; } = string.Empty;
    
    [Required]
    public string EntityType { get; set; } = string.Empty;
    
    // ... other mapping properties
}
```

**Files to Create**:
- `src/BigCommerce.Migration.Core/Models/Migration/MigrationModels.cs`
- `src/BigCommerce.Migration.Core/Models/EntityMapping/EntityMappingModels.cs`
- `src/BigCommerce.Migration.Core/Models/ApiTracking/ApiTrackingModels.cs`
- `src/BigCommerce.Migration.Core/Models/Cancellation/CancellationModels.cs`
- `tests/BigCommerce.Migration.UnitTests/Core/Models/ModelValidationTests.cs`

#### **Task 4.1.2: Add Model Validation Attributes (4-5 hours)**

**TDD Steps**:
```csharp
// RED: Write validation tests
[Test]
public void MigrationEntry_Should_Require_Valid_StoreIds()
{
    var migration = new MigrationEntry { SourceStoreId = "", DestinationStoreId = "" };
    var validationResults = ValidateModel(migration);
    
    Assert.That(validationResults.Count(v => v.ErrorMessage.Contains("SourceStoreId")), Is.EqualTo(1));
    Assert.That(validationResults.Count(v => v.ErrorMessage.Contains("DestinationStoreId")), Is.EqualTo(1));
}
```

**Implementation**:
```csharp
// GREEN: Add validation attributes
public class MigrationEntry
{
    [Required(ErrorMessage = "Migration ID is required")]
    public string Id { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Source store ID is required")]
    [StringLength(50, ErrorMessage = "Source store ID cannot exceed 50 characters")]
    public string SourceStoreId { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Destination store ID is required")]
    [StringLength(50, ErrorMessage = "Destination store ID cannot exceed 50 characters")]
    public string DestinationStoreId { get; set; } = string.Empty;
}
```

---

## 🧪 **PHASE 5: INTEGRATION & VALIDATION - Days 36-40**

### **TASK 5.1: Comprehensive Integration Testing**
**Priority**: 🔥 Critical | **Effort**: 2-3 days

#### **Task 5.1.1: Create Integration Test Suite (8-10 hours)**

**TDD Steps**:
```csharp
// Write comprehensive integration tests
[Test]
public async Task Refactored_Components_Should_Work_Together()
{
    // Test that all refactored components integrate properly
    var services = new ServiceCollection();
    services.AddBigCommerceMigrationServices();
    services.AddEntityCreationStrategies();
    
    var provider = services.BuildServiceProvider();
    
    // Test key scenarios work end-to-end
    var migrationFunctions = provider.GetRequiredService<MigrationManagementFunctions>();
    var categoryClient = provider.GetRequiredService<ICategoryApiClient>();
    var migrationRepo = provider.GetRequiredService<IMigrationRepository>();
    
    Assert.DoesNotThrow(() => /* test scenario */);
}

[Test]
public async Task Backward_Compatibility_Should_Be_Maintained()
{
    // Test that existing code still works
    var apiClient = provider.GetRequiredService<IBigCommerceApiClient>();
    var storageService = provider.GetRequiredService<IMigrationStorageService>();
    
    // Should still work with original interfaces
    await apiClient.GetCategoriesAsync(storeConfig, "tree-id");
    await storageService.CreateMigrationAsync(migration);
}
```

#### **Task 5.1.2: Performance Validation Testing (4-5 hours)**
**Test that refactoring didn't impact performance**

#### **Task 5.1.3: Create Migration Guide (3-4 hours)**
**Document the changes and migration path for consumers**

---

## 📋 **TASK TRACKING TEMPLATE**

### **Phase 1: Interface Segregation (ISP)**
- [ ] Task 1.1.1: Design and Test Segregated Interfaces
- [ ] Task 1.1.2: Update Existing IBigCommerceApiClient  
- [ ] Task 1.1.3: Create Focused Service Implementations
- [ ] Task 1.2.1: Design Repository Interfaces
- [ ] Task 1.2.2: Implement Repository Classes
- [ ] Task 1.2.3: Update IMigrationStorageService to Use Repositories
- [ ] Task 1.3.1: Update Service Registration

### **Phase 2: Single Responsibility (SRP)**
- [ ] Task 2.1.1: Split into Domain-Specific Function Classes
- [ ] Task 2.1.2: Create Shared Function Base Class
- [ ] Task 2.1.3: Update Function Registration
- [ ] Task 2.2.1: Create API Request Handler Service
- [ ] Task 2.2.2: Update BigCommerceApiClient to Use Delegation
- [ ] Task 2.3.1: Create Entity Discovery Strategy Interfaces
- [ ] Task 2.3.2: Create Strategy Factory
- [ ] Task 2.3.3: Refactor DiscoverEntitiesActivity to Use Strategy

### **Phase 3: Open/Closed Principle (OCP)**
- [ ] Task 3.1.1: Create Entity Creation Strategy Pattern
- [ ] Task 3.1.2: Refactor EntityCreateService to Use Strategy Pattern
- [ ] Task 3.1.3: Create Strategy Factory and Registration
- [ ] Task 3.2.1: Refactor Entity Transform Service
- [ ] Task 3.2.2: Refactor Entity Fetch Service

### **Phase 4: Model Organization**
- [ ] Task 4.1.1: Split StorageModels.cs into Domain Files
- [ ] Task 4.1.2: Add Model Validation Attributes

### **Phase 5: Integration & Validation**
- [ ] Task 5.1.1: Create Integration Test Suite
- [ ] Task 5.1.2: Performance Validation Testing
- [ ] Task 5.1.3: Create Migration Guide

---

## 🎯 **SUCCESS CRITERIA & DEFINITION OF DONE**

### **Per Task Completion Criteria**
- ✅ **Red Phase**: Failing tests written that define desired behavior
- ✅ **Green Phase**: Minimal implementation to pass tests
- ✅ **Refactor Phase**: Code quality improved while maintaining tests
- ✅ **Test Coverage**: 95%+ coverage maintained
- ✅ **Documentation**: Updated for new interfaces/classes

### **Per Phase Completion Criteria**
- ✅ **All tasks completed** within phase
- ✅ **Integration tests passing** 
- ✅ **No performance regression**
- ✅ **Backward compatibility maintained**
- ✅ **Code review completed**

### **Overall Project Success Criteria**
- ✅ **All SOLID violations addressed**
- ✅ **95%+ test coverage maintained**
- ✅ **No breaking changes** to public APIs
- ✅ **Performance maintained or improved**
- ✅ **Documentation updated**
- ✅ **Migration guide created**

---

## 🚀 **GETTING STARTED**

### **Prerequisites**
1. ✅ Current codebase with existing test suite
2. ✅ Development environment setup
3. ✅ Understanding of TDD principles
4. ✅ SOLID principles knowledge

### **First Steps**
1. **Create feature branch**: `feature/solid-refactoring-phase-1`
2. **Start with Task 1.1.1**: Design and Test Segregated Interfaces
3. **Follow TDD cycle**: Red → Green → Refactor
4. **Track progress** using task checklist above

### **Daily Workflow**
1. **Morning**: Review previous day's work, plan current tasks
2. **TDD Cycle**: Write failing test → Implement minimal code → Refactor
3. **Evening**: Update task tracking, commit progress, prepare next day

---

## 📞 **SUPPORT & RESOURCES**

### **Code Review Checkpoints**
- ✅ End of each phase
- ✅ After major interface changes
- ✅ Before integration testing

### **Testing Guidelines**
- **Unit Tests**: 95%+ coverage for all new code
- **Integration Tests**: All interfaces working together
- **Performance Tests**: No regression in performance
- **Backward Compatibility**: Existing APIs still functional

### **Documentation Requirements**
- **Interface Documentation**: XML comments for all public interfaces
- **Architecture Decision Records**: Document major refactoring decisions
- **Migration Guide**: Step-by-step guide for updating consumer code

---

**Ready to implement enterprise-grade SOLID principles with comprehensive TDD coverage! 🚀** 