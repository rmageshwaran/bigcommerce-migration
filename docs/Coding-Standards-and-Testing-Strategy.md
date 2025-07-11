# Coding Standards and Testing Strategy

## BigCommerce Migration System - Development Standards and Practices

---

**Document Version:** 1.0  
**Created:** January 2025  
**Purpose:** Comprehensive coding standards, SOLID principles, and unit testing strategy  
**Target Coverage:** 99% unit test coverage

---

## 🎯 **1. Coding Standards**

### **Naming Conventions**

#### **Classes and Interfaces**
```csharp
// ✅ Good - PascalCase with descriptive names
public class BigCommerceMigrationOrchestrator
public interface IBigCommerceApiClient
public class ProductTransformationService
public interface IEntityDependencyResolver

// ❌ Bad - Generic or unclear names
public class Manager
public class Helper
public class Utils
```

#### **Methods and Properties**
```csharp
// ✅ Good - PascalCase with action verbs
public async Task<MigrationResult> ProcessEntityMigrationAsync(EntityMigrationRequest request)
public bool IsValidConfiguration { get; }
public int MaxConcurrentOperations { get; set; }

// ❌ Bad - Unclear or abbreviated names
public async Task DoIt(object obj)
public bool IsValid { get; }
public int MaxOps { get; set; }
```

#### **Variables and Parameters**
```csharp
// ✅ Good - camelCase with descriptive names
var migrationRequest = new MigrationRequest();
var entityProcessingResults = new List<EntityResult>();
var bigCommerceApiClient = serviceProvider.GetService<IBigCommerceApiClient>();

// ❌ Bad - Abbreviated or unclear names
var req = new MigrationRequest();
var results = new List<EntityResult>();
var client = serviceProvider.GetService<IBigCommerceApiClient>();
```

#### **Constants and Configuration**
```csharp
// ✅ Good - UPPER_SNAKE_CASE for constants
public const int MAX_RETRY_ATTEMPTS = 3;
public const string BIGCOMMERCE_API_VERSION = "v3";
public static readonly TimeSpan DEFAULT_TIMEOUT = TimeSpan.FromMinutes(5);

// Configuration keys - kebab-case
public const string CONFIG_KEY_API_BASE_URL = "bigcommerce:api-base-url";
public const string CONFIG_KEY_BATCH_SIZE = "migration:default-batch-size";
```

### **File Organization and Structure**

#### **Directory Structure**
```
src/
├── BigCommerce.Migration.Core/
│   ├── Entities/
│   ├── Interfaces/
│   ├── Services/
│   ├── Models/
│   └── Exceptions/
├── BigCommerce.Migration.Infrastructure/
│   ├── ApiClients/
│   ├── Repositories/
│   ├── Services/
│   └── Configuration/
├── BigCommerce.Migration.Application/
│   ├── Orchestrators/
│   ├── Handlers/
│   ├── Validators/
│   └── Mappers/
└── BigCommerce.Migration.Api/
    ├── Controllers/
    ├── Middleware/
    └── Extensions/
```

#### **File Naming Convention**
```
// Services
BigCommerceApiClient.cs
ProductTransformationService.cs
EntityDependencyResolver.cs

// Interfaces
IBigCommerceApiClient.cs
IProductTransformationService.cs
IEntityDependencyResolver.cs

// Models
MigrationRequest.cs
EntityProcessingResult.cs
BigCommerceProduct.cs

// Tests
BigCommerceApiClientTests.cs
ProductTransformationServiceTests.cs
EntityDependencyResolverTests.cs
```

### **Code Formatting Standards**

#### **Method Structure**
```csharp
public async Task<MigrationResult> ProcessMigrationAsync(
    MigrationRequest request, 
    CancellationToken cancellationToken = default)
{
    // 1. Validation
    if (request == null)
        throw new ArgumentNullException(nameof(request));
    
    if (string.IsNullOrEmpty(request.MigrationId))
        throw new ArgumentException("Migration ID cannot be null or empty", nameof(request));
    
    // 2. Logging
    _logger.LogInformation("Starting migration {MigrationId} for {EntityCount} entities", 
        request.MigrationId, request.Entities.Count);
    
    // 3. Main logic
    try
    {
        var result = await ProcessEntitiesAsync(request, cancellationToken);
        
        // 4. Success logging
        _logger.LogInformation("Migration {MigrationId} completed successfully", 
            request.MigrationId);
        
        return result;
    }
    catch (Exception ex)
    {
        // 5. Error handling and logging
        _logger.LogError(ex, "Migration {MigrationId} failed", request.MigrationId);
        throw;
    }
}
```

#### **Error Handling Pattern**
```csharp
public async Task<ApiResponse<T>> CallBigCommerceApiAsync<T>(ApiRequest request)
{
    try
    {
        var response = await _httpClient.SendAsync(request.ToHttpRequest());
        
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<T>(content);
            
            return ApiResponse<T>.Success(data);
        }
        
        return ApiResponse<T>.Failed($"API call failed with status {response.StatusCode}");
    }
    catch (HttpRequestException ex)
    {
        _logger.LogError(ex, "HTTP request failed for {Endpoint}", request.Endpoint);
        return ApiResponse<T>.Failed($"Network error: {ex.Message}");
    }
    catch (TaskCanceledException ex)
    {
        _logger.LogError(ex, "Request timeout for {Endpoint}", request.Endpoint);
        return ApiResponse<T>.Failed("Request timeout");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error calling {Endpoint}", request.Endpoint);
        return ApiResponse<T>.Failed($"Unexpected error: {ex.Message}");
    }
}
```

---

## 🏗️ **2. SOLID Principles Implementation**

### **S - Single Responsibility Principle**

#### **✅ Good Implementation**
```csharp
// Each class has a single, well-defined responsibility
public class ProductTransformationService : IProductTransformationService
{
    // Only responsible for transforming products
    public async Task<BigCommerceProduct> TransformAsync(SourceProduct source)
    {
        // Transformation logic only
    }
}

public class ProductValidationService : IProductValidationService
{
    // Only responsible for validating products
    public ValidationResult ValidateProduct(BigCommerceProduct product)
    {
        // Validation logic only
    }
}

public class ProductPersistenceService : IProductPersistenceService
{
    // Only responsible for persisting products
    public async Task<bool> SaveProductAsync(BigCommerceProduct product)
    {
        // Persistence logic only
    }
}
```

#### **❌ Bad Implementation**
```csharp
// Violates SRP - too many responsibilities
public class ProductService
{
    public async Task<BigCommerceProduct> TransformAsync(SourceProduct source) { }
    public ValidationResult ValidateProduct(BigCommerceProduct product) { }
    public async Task<bool> SaveProductAsync(BigCommerceProduct product) { }
    public async Task<bool> SendEmailNotification(string email) { }
    public void LogActivity(string message) { }
}
```

### **O - Open/Closed Principle**

#### **✅ Good Implementation**
```csharp
// Abstract base class open for extension
public abstract class EntityTransformationService<TSource, TTarget>
{
    protected abstract Task<TTarget> TransformCoreAsync(TSource source);
    
    public async Task<TransformationResult<TTarget>> TransformAsync(TSource source)
    {
        try
        {
            var result = await TransformCoreAsync(source);
            return TransformationResult<TTarget>.Success(result);
        }
        catch (Exception ex)
        {
            return TransformationResult<TTarget>.Failed(ex.Message);
        }
    }
}

// Concrete implementations - closed for modification, open for extension
public class ProductTransformationService : EntityTransformationService<SourceProduct, BigCommerceProduct>
{
    protected override async Task<BigCommerceProduct> TransformCoreAsync(SourceProduct source)
    {
        // Product-specific transformation logic
    }
}

public class CategoryTransformationService : EntityTransformationService<SourceCategory, BigCommerceCategory>
{
    protected override async Task<BigCommerceCategory> TransformCoreAsync(SourceCategory source)
    {
        // Category-specific transformation logic
    }
}
```

### **L - Liskov Substitution Principle**

#### **✅ Good Implementation**
```csharp
public interface IApiClient
{
    Task<ApiResponse<T>> GetAsync<T>(string endpoint);
    Task<ApiResponse<T>> PostAsync<T>(string endpoint, object data);
}

public class BigCommerceApiClient : IApiClient
{
    public async Task<ApiResponse<T>> GetAsync<T>(string endpoint)
    {
        // Implementation that maintains contract
        // - Returns ApiResponse<T>
        // - Handles all expected scenarios
        // - Doesn't throw unexpected exceptions
    }
    
    public async Task<ApiResponse<T>> PostAsync<T>(string endpoint, object data)
    {
        // Implementation that maintains contract
    }
}

// Can be substituted anywhere IApiClient is expected
public class MigrationService
{
    private readonly IApiClient _apiClient;
    
    public MigrationService(IApiClient apiClient)
    {
        _apiClient = apiClient; // Works with any IApiClient implementation
    }
}
```

### **I - Interface Segregation Principle**

#### **✅ Good Implementation**
```csharp
// Segregated interfaces - clients only depend on what they need
public interface IProductReader
{
    Task<Product> GetProductAsync(string id);
    Task<IEnumerable<Product>> GetProductsAsync(ProductFilter filter);
}

public interface IProductWriter
{
    Task<bool> CreateProductAsync(Product product);
    Task<bool> UpdateProductAsync(Product product);
    Task<bool> DeleteProductAsync(string id);
}

public interface IProductValidator
{
    ValidationResult ValidateProduct(Product product);
}

// Services only implement interfaces they need
public class ProductMigrationService : IProductReader, IProductWriter
{
    // Only implements read/write operations
}

public class ProductValidationService : IProductValidator
{
    // Only implements validation
}
```

### **D - Dependency Inversion Principle**

#### **✅ Good Implementation**
```csharp
// High-level modules depend on abstractions
public class MigrationOrchestrator
{
    private readonly IEntityDependencyResolver _dependencyResolver;
    private readonly ITransformationService _transformationService;
    private readonly IValidationService _validationService;
    private readonly IBigCommerceApiClient _apiClient;
    
    public MigrationOrchestrator(
        IEntityDependencyResolver dependencyResolver,
        ITransformationService transformationService,
        IValidationService validationService,
        IBigCommerceApiClient apiClient)
    {
        _dependencyResolver = dependencyResolver;
        _transformationService = transformationService;
        _validationService = validationService;
        _apiClient = apiClient;
    }
}

// Dependency injection configuration
public void ConfigureServices(IServiceCollection services)
{
    // Register interfaces with implementations
    services.AddScoped<IEntityDependencyResolver, EntityDependencyResolver>();
    services.AddScoped<ITransformationService, ProductTransformationService>();
    services.AddScoped<IValidationService, ProductValidationService>();
    services.AddScoped<IBigCommerceApiClient, BigCommerceApiClient>();
}
```

---

## 🧪 **3. Unit Test Coverage Strategy (99%)**

### **Test Structure and Organization**

#### **Test Project Structure**
```
tests/
├── BigCommerce.Migration.Core.Tests/
│   ├── Services/
│   ├── Entities/
│   ├── Validators/
│   └── TestHelpers/
├── BigCommerce.Migration.Infrastructure.Tests/
│   ├── ApiClients/
│   ├── Repositories/
│   └── TestHelpers/
└── BigCommerce.Migration.Application.Tests/
    ├── Orchestrators/
    ├── Handlers/
    └── TestHelpers/
```

#### **Test Naming Convention**
```csharp
// Pattern: MethodName_StateUnderTest_ExpectedBehavior
[Test]
public async Task TransformProductAsync_WithValidProduct_ReturnsTransformedProduct()
{
    // Arrange
    var sourceProduct = CreateValidSourceProduct();
    var expectedProduct = CreateExpectedBigCommerceProduct();
    
    // Act
    var result = await _productTransformationService.TransformAsync(sourceProduct);
    
    // Assert
    Assert.That(result.IsSuccess, Is.True);
    Assert.That(result.Value.Name, Is.EqualTo(expectedProduct.Name));
}

[Test]
public async Task TransformProductAsync_WithNullProduct_ThrowsArgumentNullException()
{
    // Act & Assert
    var ex = await Assert.ThrowsAsync<ArgumentNullException>(
        () => _productTransformationService.TransformAsync(null));
    
    Assert.That(ex.ParamName, Is.EqualTo("product"));
}
```

### **Comprehensive Test Categories**

#### **1. Unit Tests (70% of coverage)**
```csharp
[TestFixture]
public class ProductTransformationServiceTests
{
    private ProductTransformationService _service;
    private Mock<ILogger<ProductTransformationService>> _mockLogger;
    private Mock<IConfiguration> _mockConfiguration;
    
    [SetUp]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<ProductTransformationService>>();
        _mockConfiguration = new Mock<IConfiguration>();
        _service = new ProductTransformationService(_mockLogger.Object, _mockConfiguration.Object);
    }
    
    [Test]
    public async Task TransformAsync_WithValidProduct_ReturnsSuccess()
    {
        // Arrange
        var sourceProduct = new SourceProduct
        {
            Id = "123",
            Name = "Test Product",
            Price = 99.99m
        };
        
        // Act
        var result = await _service.TransformAsync(sourceProduct);
        
        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Name, Is.EqualTo("Test Product"));
        Assert.That(result.Value.Price, Is.EqualTo(99.99m));
    }
    
    [Test]
    public async Task TransformAsync_WithEmptyName_ReturnsFailure()
    {
        // Arrange
        var sourceProduct = new SourceProduct
        {
            Id = "123",
            Name = "",
            Price = 99.99m
        };
        
        // Act
        var result = await _service.TransformAsync(sourceProduct);
        
        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.ErrorMessage, Contains.Substring("Name cannot be empty"));
    }
    
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task TransformAsync_WithInvalidName_ReturnsFailure(string invalidName)
    {
        // Arrange
        var sourceProduct = new SourceProduct
        {
            Id = "123",
            Name = invalidName,
            Price = 99.99m
        };
        
        // Act
        var result = await _service.TransformAsync(sourceProduct);
        
        // Assert
        Assert.That(result.IsSuccess, Is.False);
    }
}
```

#### **2. Integration Tests (20% of coverage)**
```csharp
[TestFixture]
public class BigCommerceApiClientIntegrationTests
{
    private BigCommerceApiClient _client;
    private TestServer _testServer;
    private HttpClient _httpClient;
    
    [SetUp]
    public void Setup()
    {
        var hostBuilder = new WebHostBuilder()
            .UseStartup<TestStartup>();
            
        _testServer = new TestServer(hostBuilder);
        _httpClient = _testServer.CreateClient();
        
        _client = new BigCommerceApiClient(_httpClient, Mock.Of<ILogger<BigCommerceApiClient>>());
    }
    
    [Test]
    public async Task GetProductAsync_WithValidId_ReturnsProduct()
    {
        // Arrange
        var productId = "123";
        
        // Act
        var result = await _client.GetProductAsync(productId);
        
        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Id, Is.EqualTo(productId));
    }
    
    [TearDown]
    public void TearDown()
    {
        _httpClient?.Dispose();
        _testServer?.Dispose();
    }
}
```

#### **3. End-to-End Tests (10% of coverage)**
```csharp
[TestFixture]
public class MigrationEndToEndTests
{
    private WebApplicationFactory<Program> _factory;
    private HttpClient _client;
    
    [SetUp]
    public void Setup()
    {
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }
    
    [Test]
    public async Task CompleteMigration_WithValidRequest_CompletesSuccessfully()
    {
        // Arrange
        var migrationRequest = new MigrationRequest
        {
            MigrationId = Guid.NewGuid().ToString(),
            SourceStoreId = "source-123",
            DestinationStoreId = "dest-456",
            Entities = new[] { "Products", "Categories" }
        };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/migration", migrationRequest);
        
        // Assert
        Assert.That(response.IsSuccessStatusCode, Is.True);
        
        var result = await response.Content.ReadFromJsonAsync<MigrationResult>();
        Assert.That(result.IsSuccess, Is.True);
    }
}
```

### **Test Coverage Tools and Configuration**

#### **Coverage Configuration (coverlet.json)**
```json
{
  "exclude": [
    "[*]*.Migrations.*",
    "[*]*.Program",
    "[*]*.Startup",
    "[*]*Tests*"
  ],
  "include": [
    "[BigCommerce.Migration.Core]*",
    "[BigCommerce.Migration.Infrastructure]*",
    "[BigCommerce.Migration.Application]*"
  ],
  "threshold": 99.0,
  "thresholdType": "line",
  "thresholdStat": "total"
}
```

#### **Test Execution Script**
```bash
# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage

# Generate coverage report
reportgenerator -reports:"coverage/**/coverage.cobertura.xml" -targetdir:"coverage/report" -reporttypes:"Html;Cobertura"

# Check coverage threshold
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Threshold=99
```

### **Test Helpers and Utilities**

#### **Test Data Builders**
```csharp
public class ProductTestDataBuilder
{
    private string _id = "default-id";
    private string _name = "Default Product";
    private decimal _price = 99.99m;
    private List<string> _categories = new();
    
    public ProductTestDataBuilder WithId(string id)
    {
        _id = id;
        return this;
    }
    
    public ProductTestDataBuilder WithName(string name)
    {
        _name = name;
        return this;
    }
    
    public ProductTestDataBuilder WithPrice(decimal price)
    {
        _price = price;
        return this;
    }
    
    public ProductTestDataBuilder WithCategories(params string[] categories)
    {
        _categories.AddRange(categories);
        return this;
    }
    
    public SourceProduct BuildSourceProduct()
    {
        return new SourceProduct
        {
            Id = _id,
            Name = _name,
            Price = _price,
            Categories = _categories
        };
    }
    
    public BigCommerceProduct BuildBigCommerceProduct()
    {
        return new BigCommerceProduct
        {
            Id = _id,
            Name = _name,
            Price = _price,
            Categories = _categories.ToArray()
        };
    }
}

// Usage in tests
[Test]
public async Task TransformAsync_WithComplexProduct_TransformsCorrectly()
{
    // Arrange
    var sourceProduct = new ProductTestDataBuilder()
        .WithId("complex-123")
        .WithName("Complex Product")
        .WithPrice(199.99m)
        .WithCategories("Electronics", "Computers")
        .BuildSourceProduct();
    
    // Act
    var result = await _service.TransformAsync(sourceProduct);
    
    // Assert
    Assert.That(result.IsSuccess, Is.True);
    Assert.That(result.Value.Categories.Length, Is.EqualTo(2));
}
```

#### **Mock Service Factory**
```csharp
public static class MockServiceFactory
{
    public static Mock<IBigCommerceApiClient> CreateMockApiClient()
    {
        var mock = new Mock<IBigCommerceApiClient>();
        
        mock.Setup(x => x.GetProductAsync(It.IsAny<string>()))
            .ReturnsAsync(ApiResponse<BigCommerceProduct>.Success(new BigCommerceProduct()));
            
        mock.Setup(x => x.CreateProductAsync(It.IsAny<BigCommerceProduct>()))
            .ReturnsAsync(ApiResponse<BigCommerceProduct>.Success(new BigCommerceProduct()));
            
        return mock;
    }
    
    public static Mock<ILogger<T>> CreateMockLogger<T>()
    {
        return new Mock<ILogger<T>>();
    }
}
```

### **Coverage Monitoring and CI/CD Integration**

#### **Azure DevOps Pipeline**
```yaml
# azure-pipelines.yml
trigger:
  - main
  - develop

pool:
  vmImage: 'ubuntu-latest'

steps:
- task: DotNetCoreCLI@2
  displayName: 'Restore packages'
  inputs:
    command: 'restore'
    projects: '**/*.csproj'

- task: DotNetCoreCLI@2
  displayName: 'Build solution'
  inputs:
    command: 'build'
    projects: '**/*.csproj'
    arguments: '--configuration Release --no-restore'

- task: DotNetCoreCLI@2
  displayName: 'Run tests with coverage'
  inputs:
    command: 'test'
    projects: '**/*Tests.csproj'
    arguments: '--configuration Release --no-build --collect:"XPlat Code Coverage" --results-directory ./coverage'
  
- task: PublishCodeCoverageResults@1
  displayName: 'Publish coverage results'
  inputs:
    codeCoverageTool: 'Cobertura'
    summaryFileLocation: 'coverage/**/coverage.cobertura.xml'
    reportDirectory: 'coverage/report'
    failIfCoverageEmpty: true

- task: BuildQualityChecks@8
  displayName: 'Check coverage threshold'
  inputs:
    checkCoverage: true
    coverageFailOption: 'fixed'
    coverageThreshold: '99'
```

### **Coverage Exclusions Strategy**

#### **Acceptable Exclusions (1% allowance)**
```csharp
// 1. Program.cs and Startup.cs
[ExcludeFromCodeCoverage]
public class Program
{
    public static void Main(string[] args) { }
}

// 2. Exception classes (constructors only)
[ExcludeFromCodeCoverage]
public class MigrationException : Exception
{
    public MigrationException(string message) : base(message) { }
}

// 3. Simple property getters/setters
public class MigrationRequest
{
    public string MigrationId { get; set; }
    public string SourceStoreId { get; set; }
    public string DestinationStoreId { get; set; }
}
```

## 📊 **Quality Metrics and Monitoring**

### **Code Quality Targets**
- **Unit Test Coverage**: 99%
- **Cyclomatic Complexity**: < 10 per method
- **Maintainability Index**: > 80
- **Code Duplication**: < 5%
- **Technical Debt**: < 1 hour per 1000 lines

### **Automated Quality Checks**
```csharp
// SonarQube integration
dotnet sonarscanner begin /k:"BigCommerce.Migration" /d:sonar.host.url="https://sonarqube.company.com" /d:sonar.login="token"
dotnet build
dotnet test --collect:"XPlat Code Coverage"
dotnet sonarscanner end /d:sonar.login="token"
```

This comprehensive approach ensures we maintain the highest coding standards while achieving 99% unit test coverage for our BigCommerce Migration System.

---

**Document Status**: Final  
**Review Cycle**: Monthly  
**Next Review**: February 2025 