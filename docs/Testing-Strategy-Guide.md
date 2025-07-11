# BigCommerce Migration System - Testing Strategy Guide

## Overview

This guide provides comprehensive testing strategies for validating all workflows in the BigCommerce migration system, including entity configuration management, timeout prevention, granular cancellation, and complex product processing.

## Table of Contents
1. [Testing Architecture](#testing-architecture)
2. [Unit Testing Strategy](#unit-testing-strategy)
3. [Integration Testing](#integration-testing)
4. [End-to-End Testing](#end-to-end-testing)
5. [Performance Testing](#performance-testing)
6. [Chaos Testing](#chaos-testing)
7. [Test Data Management](#test-data-management)
8. [Monitoring & Validation](#monitoring--validation)
9. [Test Automation](#test-automation)
10. [Testing Tools & Frameworks](#testing-tools--frameworks)

## Testing Architecture

### Testing Pyramid Structure

```
┌─────────────────────────────────────┐
│         E2E Tests (Few)             │ ← Full workflow validation
├─────────────────────────────────────┤
│      Integration Tests (Some)       │ ← Orchestration testing  
├─────────────────────────────────────┤
│        Unit Tests (Many)            │ ← Component testing
└─────────────────────────────────────┘
```

### Test Environment Strategy

**1. Local Development Environment**
- Azure Storage Emulator (Azurite)
- Local OpenSearch instance
- Mock BigCommerce API endpoints
- Azure Functions Core Tools

**2. Development Environment**
- Azure Functions App (Consumption Plan)
- Azure Storage Account
- OpenSearch Service (small instance)
- BigCommerce sandbox stores

**3. Staging Environment**
- Full Azure infrastructure
- Production-like configuration
- Real BigCommerce test stores
- Performance monitoring enabled

**4. Production Environment**
- Production configuration
- Real BigCommerce stores
- Full monitoring and alerting

## Unit Testing Strategy

### 1. Activity Functions Testing

**Test Framework:** xUnit + FluentAssertions + Moq

```csharp
[TestClass]
public class ProcessVariantBatchTests
{
    private readonly Mock<IBigCommerceService> _mockBigCommerceService;
    private readonly Mock<IOpenSearchService> _mockOpenSearchService;
    private readonly Mock<ICancellationTokenManager> _mockCancellationManager;
    
    [TestMethod]
    public async Task ProcessVariantBatch_WithValidVariants_ProcessesSuccessfully()
    {
        // Arrange
        var variants = CreateTestVariants(10);
        var migrationId = Guid.NewGuid().ToString();
        
        _mockCancellationManager
            .Setup(x => x.IsCancellationRequested(migrationId))
            .ReturnsAsync(false);
            
        _mockBigCommerceService
            .Setup(x => x.CreateVariantAsync(It.IsAny<Variant>()))
            .ReturnsAsync(new ApiResponse<Variant> { Success = true });
        
        // Act
        var result = await _processVariantBatchFunction.Run(
            new { MigrationId = migrationId, Variants = variants });
        
        // Assert
        result.Should().NotBeNull();
        result.ProcessedCount.Should().Be(10);
        result.ErrorCount.Should().Be(0);
        
        _mockBigCommerceService.Verify(
            x => x.CreateVariantAsync(It.IsAny<Variant>()), 
            Times.Exactly(10));
    }
    
    [TestMethod]
    public async Task ProcessVariantBatch_WithCancellation_StopsProcessing()
    {
        // Arrange
        var variants = CreateTestVariants(10);
        var migrationId = Guid.NewGuid().ToString();
        
        _mockCancellationManager
            .SetupSequence(x => x.IsCancellationRequested(migrationId))
            .ReturnsAsync(false) // First variant processes
            .ReturnsAsync(false) // Second variant processes  
            .ReturnsAsync(true); // Third variant cancelled
        
        // Act
        var result = await _processVariantBatchFunction.Run(
            new { MigrationId = migrationId, Variants = variants });
        
        // Assert
        result.ProcessedCount.Should().Be(2);
        result.CancelledCount.Should().Be(8);
        
        _mockBigCommerceService.Verify(
            x => x.CreateVariantAsync(It.IsAny<Variant>()), 
            Times.Exactly(2));
    }
}
```

### 2. Configuration Management Testing

```csharp
[TestClass]
public class EntityConfigurationManagerTests
{
    [TestMethod]
    public async Task ValidateConfiguration_WithValidSettings_ReturnsValid()
    {
        // Arrange
        var config = new EntityConfiguration
        {
            EntityType = "Products",
            BatchSize = 10,
            MaxConcurrency = 4,
            Priority = 2
        };
        
        // Act
        var result = await _configManager.ValidateConfiguration(config);
        
        // Assert
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }
    
    [TestMethod]
    public async Task ValidateConfiguration_WithInvalidBatchSize_ReturnsErrors()
    {
        // Arrange
        var config = new EntityConfiguration
        {
            EntityType = "Products",
            BatchSize = 100, // Exceeds max for products
            MaxConcurrency = 4
        };
        
        // Act
        var result = await _configManager.ValidateConfiguration(config);
        
        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Batch size exceeds maximum"));
    }
}
```

### 3. Rate Limiting Testing

```csharp
[TestClass]
public class RateLimiterTests
{
    [TestMethod]
    public async Task CheckRateLimit_WithinLimits_AllowsRequest()
    {
        // Arrange
        var rateLimiter = new RateLimiter(_mockTableStorage.Object);
        
        // Act
        var result = await rateLimiter.CanMakeRequestAsync();
        
        // Assert
        result.CanProceed.Should().BeTrue();
        result.DelayRequired.Should().Be(TimeSpan.Zero);
    }
    
    [TestMethod]
    public async Task CheckRateLimit_ExceedsLimits_RequiresDelay()
    {
        // Arrange
        var rateLimiter = new RateLimiter(_mockTableStorage.Object);
        
        // Simulate 12 requests in current window
        for (int i = 0; i < 12; i++)
        {
            await rateLimiter.RecordRequestAsync();
        }
        
        // Act
        var result = await rateLimiter.CanMakeRequestAsync();
        
        // Assert
        result.CanProceed.Should().BeFalse();
        result.DelayRequired.Should().BeGreaterThan(TimeSpan.Zero);
    }
}
```

## Integration Testing

### 1. Orchestration Testing

**Test Framework:** Azure Functions Test Framework + TestContainers

```csharp
[TestClass]
public class ProcessComplexProductOrchestrationTests
{
    private TestHost _testHost;
    private TestContainer _storageContainer;
    private TestContainer _openSearchContainer;
    
    [TestInitialize]
    public async Task Setup()
    {
        // Start test containers
        _storageContainer = new TestcontainersBuilder<GenericContainer>()
            .WithImage("mcr.microsoft.com/azure-storage/azurite")
            .WithPortBinding(10000, 10000)
            .Build();
            
        await _storageContainer.StartAsync();
        
        // Configure test host
        _testHost = new TestHost();
        _testHost.Services.Configure<StorageOptions>(options =>
        {
            options.ConnectionString = _storageContainer.GetConnectionString();
        });
    }
    
    [TestMethod]
    public async Task ProcessComplexProduct_With500Variants_CompletesWithoutTimeout()
    {
        // Arrange
        var product = CreateComplexProduct(500, 1000); // 500 variants, 1000 images
        var migrationId = Guid.NewGuid().ToString();
        
        var mockBigCommerceService = new Mock<IBigCommerceService>();
        mockBigCommerceService
            .Setup(x => x.CreateVariantAsync(It.IsAny<Variant>()))
            .ReturnsAsync(new ApiResponse<Variant> { Success = true });
        
        // Act
        var result = await _testHost.CallOrchestratorAsync(
            "ProcessComplexProduct",
            new { MigrationId = migrationId, Product = product });
        
        // Assert
        result.Status.Should().Be(OrchestrationStatus.Completed);
        result.ExecutionTime.Should().BeLessThan(TimeSpan.FromMinutes(30)); // No timeout
        
        // Verify sub-orchestrations were created
        var subOrchestrations = await GetSubOrchestrations(result.InstanceId);
        subOrchestrations.Should().HaveCount(2); // Variants + Images
    }
    
    [TestMethod]
    public async Task ProcessComplexProduct_WithCancellation_StopsGracefully()
    {
        // Arrange
        var product = CreateComplexProduct(100, 200);
        var migrationId = Guid.NewGuid().ToString();
        
        // Act
        var orchestrationTask = _testHost.CallOrchestratorAsync(
            "ProcessComplexProduct",
            new { MigrationId = migrationId, Product = product });
        
        // Wait for processing to start, then cancel
        await Task.Delay(TimeSpan.FromSeconds(5));
        await _testHost.CallActivityAsync("RequestCancellation", migrationId);
        
        var result = await orchestrationTask;
        
        // Assert
        result.Status.Should().Be(OrchestrationStatus.Completed);
        
        var cancellationStatus = await GetCancellationStatus(migrationId);
        cancellationStatus.Should().NotBeNull();
        cancellationStatus.Status.Should().Be("Cancelled");
    }
}
```

### 2. Sub-Orchestration Testing

```csharp
[TestClass]
public class VariantsSubOrchestrationTests
{
    [TestMethod]
    public async Task ProcessVariantsOrchestrator_WithBatching_ProcessesInChunks()
    {
        // Arrange
        var variants = CreateTestVariants(50);
        var migrationId = Guid.NewGuid().ToString();
        
        // Act
        var result = await _testHost.CallOrchestratorAsync(
            "ProcessVariantsWithCancellation",
            new { MigrationId = migrationId, Variants = variants });
        
        // Assert
        result.Status.Should().Be(OrchestrationStatus.Completed);
        
        // Verify batch processing (50 variants ÷ 10 per batch = 5 batches)
        var activityCalls = await GetActivityCalls(result.InstanceId, "ProcessVariantBatch");
        activityCalls.Should().HaveCount(5);
        
        // Verify rate limiting delays
        var timerCalls = await GetTimerCalls(result.InstanceId);
        timerCalls.Should().HaveCount(5); // One delay per batch
    }
}
```

## End-to-End Testing

### 1. Complete Migration Workflow

```csharp
[TestClass]
public class EndToEndMigrationTests
{
    private TestEnvironment _testEnv;
    
    [TestInitialize]
    public async Task Setup()
    {
        _testEnv = await TestEnvironment.CreateAsync();
        await _testEnv.SeedTestData();
    }
    
    [TestMethod]
    public async Task CompleteMigration_SmallDataset_CompletesSuccessfully()
    {
        // Arrange
        var migrationRequest = new MigrationRequest
        {
            SourceStore = _testEnv.SourceStoreUrl,
            DestinationStore = _testEnv.DestinationStoreUrl,
            Entities = new[]
            {
                new EntityConfig { EntityType = "Categories", BatchSize = 10, MaxConcurrency = 1 },
                new EntityConfig { EntityType = "Products", BatchSize = 5, MaxConcurrency = 2 },
                new EntityConfig { EntityType = "Variants", BatchSize = 10, MaxConcurrency = 1 }
            }
        };
        
        // Act
        var migrationId = await _testEnv.StartMigration(migrationRequest);
        var result = await _testEnv.WaitForCompletion(migrationId, TimeSpan.FromMinutes(10));
        
        // Assert
        result.Status.Should().Be("Completed");
        result.SuccessCount.Should().BeGreaterThan(0);
        result.ErrorCount.Should().Be(0);
        
        // Verify data in destination store
        var destinationCategories = await _testEnv.GetDestinationCategories();
        var destinationProducts = await _testEnv.GetDestinationProducts();
        
        destinationCategories.Should().HaveCount(result.CategoryCount);
        destinationProducts.Should().HaveCount(result.ProductCount);
    }
    
    [TestMethod]
    public async Task CompleteMigration_WithCancellation_CancelsGracefully()
    {
        // Arrange
        var migrationRequest = CreateLargeMigrationRequest();
        
        // Act
        var migrationId = await _testEnv.StartMigration(migrationRequest);
        
        // Wait for processing to start
        await _testEnv.WaitForStatus(migrationId, "InProgress", TimeSpan.FromMinutes(2));
        
        // Cancel migration
        var cancellationResult = await _testEnv.CancelMigration(migrationId);
        var finalResult = await _testEnv.WaitForCompletion(migrationId, TimeSpan.FromMinutes(5));
        
        // Assert
        cancellationResult.Status.Should().Be("Cancelling");
        finalResult.Status.Should().Be("Cancelled");
        finalResult.ProcessedCount.Should().BeGreaterThan(0);
        finalResult.CancelledCount.Should().BeGreaterThan(0);
        
        // Verify graceful shutdown
        var errorLogs = await _testEnv.GetErrorLogs(migrationId);
        errorLogs.Where(e => e.Type == "CancellationError").Should().BeEmpty();
    }
}
```

### 2. Complex Product Processing

```csharp
[TestMethod]
public async Task ComplexProductMigration_With500Components_CompletesWithoutTimeout()
{
    // Arrange
    var complexProduct = await _testEnv.CreateComplexProduct(500, 1000);
    var migrationRequest = new MigrationRequest
    {
        SourceStore = _testEnv.SourceStoreUrl,
        DestinationStore = _testEnv.DestinationStoreUrl,
        Entities = new[]
        {
            new EntityConfig { EntityType = "Products", BatchSize = 1, MaxConcurrency = 1 },
            new EntityConfig { EntityType = "Variants", BatchSize = 10, MaxConcurrency = 2 },
            new EntityConfig { EntityType = "Images", BatchSize = 2, MaxConcurrency = 1 }
        },
        Filters = new { ProductIds = new[] { complexProduct.Id } }
    };
    
    // Act
    var migrationId = await _testEnv.StartMigration(migrationRequest);
    var result = await _testEnv.WaitForCompletion(migrationId, TimeSpan.FromHours(1));
    
    // Assert
    result.Status.Should().Be("Completed");
    
    // Verify sub-orchestrations were used
    var subOrchestrations = await _testEnv.GetSubOrchestrations(migrationId);
    subOrchestrations.Should().ContainSingle(s => s.Name == "ProcessVariantsWithCancellation");
    subOrchestrations.Should().ContainSingle(s => s.Name == "ProcessImagesWithCancellation");
    
    // Verify all components were processed
    var destinationVariants = await _testEnv.GetProductVariants(complexProduct.Id);
    var destinationImages = await _testEnv.GetProductImages(complexProduct.Id);
    
    destinationVariants.Should().HaveCount(500);
    destinationImages.Should().HaveCount(1000);
}
```

## Performance Testing

### 1. Load Testing with NBomber

```csharp
[TestClass]
public class PerformanceTests
{
    [TestMethod]
    public async Task MigrationThroughput_UnderLoad_MaintainsPerformance()
    {
        var scenario = Scenario.Create("migration_load_test", async context =>
        {
            var migrationRequest = CreateRandomMigrationRequest();
            var migrationId = await _testEnv.StartMigration(migrationRequest);
            
            // Monitor until completion or timeout
            var result = await _testEnv.WaitForCompletion(migrationId, TimeSpan.FromMinutes(30));
            
            return result.Status == "Completed" ? Response.Ok() : Response.Fail();
        })
        .WithLoadSimulations(
            Simulation.KeepConstant(copies: 5, during: TimeSpan.FromMinutes(15))
        );
        
        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();
        
        // Assert performance metrics
        stats.AllOkCount.Should().BeGreaterThan(0);
        stats.AllFailCount.Should().BeLessThan(stats.AllOkCount * 0.05); // < 5% failure rate
        stats.ScenarioStats[0].Ok.Response.Mean.Should().BeLessThan(TimeSpan.FromMinutes(10));
    }
    
    [TestMethod]
    public async Task RateLimiting_UnderHighLoad_RespectsLimits()
    {
        var scenario = Scenario.Create("rate_limit_test", async context =>
        {
            var response = await _testEnv.MakeApiCall();
            return response.IsSuccess ? Response.Ok() : Response.Fail();
        })
        .WithLoadSimulations(
            Simulation.KeepConstant(copies: 20, during: TimeSpan.FromMinutes(5))
        );
        
        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();
        
        // Verify rate limit compliance (should be ~12 requests per second)
        var requestsPerSecond = stats.AllOkCount / stats.ScenarioStats[0].Duration.TotalSeconds;
        requestsPerSecond.Should().BeLessOrEqualTo(12.5); // Allow small margin
    }
}
```

### 2. Stress Testing

```csharp
[TestMethod]
public async Task LargeMigration_10KProducts_CompletesWithinSLA()
{
    // Arrange
    var largeMigrationRequest = new MigrationRequest
    {
        SourceStore = _testEnv.SourceStoreUrl,
        DestinationStore = _testEnv.DestinationStoreUrl,
        Entities = new[]
        {
            new EntityConfig { EntityType = "Categories", BatchSize = 50, MaxConcurrency = 2 },
            new EntityConfig { EntityType = "Products", BatchSize = 10, MaxConcurrency = 4 },
            new EntityConfig { EntityType = "Variants", BatchSize = 20, MaxConcurrency = 3 },
            new EntityConfig { EntityType = "Images", BatchSize = 5, MaxConcurrency = 2 }
        }
    };
    
    await _testEnv.SeedLargeDataset(10000); // 10K products with variants and images
    
    // Act
    var startTime = DateTime.UtcNow;
    var migrationId = await _testEnv.StartMigration(largeMigrationRequest);
    var result = await _testEnv.WaitForCompletion(migrationId, TimeSpan.FromHours(8));
    var completionTime = DateTime.UtcNow - startTime;
    
    // Assert
    result.Status.Should().Be("Completed");
    completionTime.Should().BeLessThan(TimeSpan.FromHours(6)); // SLA: < 6 hours for 10K products
    
    // Verify resource utilization
    var resourceMetrics = await _testEnv.GetResourceMetrics(migrationId);
    resourceMetrics.PeakMemoryUsage.Should().BeLessThan(512); // MB
    resourceMetrics.PeakCpuUsage.Should().BeLessThan(80); // Percentage
}
```

## Chaos Testing

### 1. Resilience Testing

```csharp
[TestClass]
public class ChaosTests
{
    [TestMethod]
    public async Task Migration_WithRandomAPIFailures_RecoversProperly()
    {
        // Arrange
        var chaosService = new ChaosService();
        chaosService.ConfigureRandomFailures(
            failureRate: 0.1, // 10% failure rate
            services: new[] { "BigCommerceAPI" }
        );
        
        var migrationRequest = CreateStandardMigrationRequest();
        
        // Act
        var migrationId = await _testEnv.StartMigration(migrationRequest);
        var result = await _testEnv.WaitForCompletion(migrationId, TimeSpan.FromMinutes(20));
        
        // Assert
        result.Status.Should().Be("Completed");
        result.ErrorCount.Should().BeGreaterThan(0); // Some errors expected
        result.RetryCount.Should().BeGreaterThan(0); // Should have retried
        result.SuccessRate.Should().BeGreaterThan(0.9); // > 90% success despite failures
    }
    
    [TestMethod]
    public async Task Migration_WithStorageOutage_RecoversWhenRestored()
    {
        // Arrange
        var migrationRequest = CreateStandardMigrationRequest();
        var migrationId = await _testEnv.StartMigration(migrationRequest);
        
        // Wait for processing to start
        await _testEnv.WaitForStatus(migrationId, "InProgress", TimeSpan.FromMinutes(2));
        
        // Act - Simulate storage outage
        await _testEnv.SimulateStorageOutage(TimeSpan.FromMinutes(2));
        
        // Wait for recovery and completion
        var result = await _testEnv.WaitForCompletion(migrationId, TimeSpan.FromMinutes(15));
        
        // Assert
        result.Status.Should().Be("Completed");
        
        // Verify recovery metrics
        var recoveryMetrics = await _testEnv.GetRecoveryMetrics(migrationId);
        recoveryMetrics.OutageDetectionTime.Should().BeLessThan(TimeSpan.FromMinutes(1));
        recoveryMetrics.RecoveryTime.Should().BeLessThan(TimeSpan.FromMinutes(5));
    }
}
```

## Test Data Management

### 1. Test Data Factory

```csharp
public class TestDataFactory
{
    public static Product CreateSimpleProduct(int id = 1)
    {
        return new Product
        {
            Id = id,
            Name = $"Test Product {id}",
            Price = 19.99m,
            CategoryIds = new[] { 1, 2 },
            BrandId = 1,
            Variants = CreateVariants(3),
            Images = CreateImages(2)
        };
    }
    
    public static Product CreateComplexProduct(int variantCount, int imageCount)
    {
        return new Product
        {
            Id = 999,
            Name = "Complex Test Product",
            Price = 199.99m,
            CategoryIds = new[] { 1, 2, 3 },
            BrandId = 1,
            Variants = CreateVariants(variantCount),
            Images = CreateImages(imageCount),
            Modifiers = CreateModifiers(50)
        };
    }
    
    public static List<Variant> CreateVariants(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new Variant
            {
                Id = i,
                Sku = $"TEST-VAR-{i:D4}",
                Price = 19.99m + i,
                InventoryLevel = 100
            })
            .ToList();
    }
    
    public static List<ProductImage> CreateImages(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new ProductImage
            {
                Id = i,
                ImageUrl = $"https://test.com/image-{i}.jpg",
                IsThumbnail = i == 1,
                SortOrder = i
            })
            .ToList();
    }
}
```

### 2. Test Environment Management

```csharp
public class TestEnvironment
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    
    public static async Task<TestEnvironment> CreateAsync()
    {
        var testEnv = new TestEnvironment();
        await testEnv.InitializeAsync();
        return testEnv;
    }
    
    public async Task SeedTestData()
    {
        // Create test categories
        await CreateTestCategories(10);
        
        // Create test brands
        await CreateTestBrands(5);
        
        // Create test products
        await CreateTestProducts(100);
    }
    
    public async Task SeedLargeDataset(int productCount)
    {
        // Create categories
        await CreateTestCategories(50);
        
        // Create brands  
        await CreateTestBrands(20);
        
        // Create products with variants and images
        for (int i = 0; i < productCount; i++)
        {
            var product = TestDataFactory.CreateSimpleProduct(i + 1);
            await CreateTestProduct(product);
        }
    }
    
    public async Task<string> StartMigration(MigrationRequest request)
    {
        var response = await _httpClient.PostAsync("/migrations", 
            JsonContent.Create(request));
        
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<MigrationResponse>();
        return result.MigrationId;
    }
    
    public async Task<MigrationStatus> WaitForCompletion(string migrationId, TimeSpan timeout)
    {
        var endTime = DateTime.UtcNow.Add(timeout);
        
        while (DateTime.UtcNow < endTime)
        {
            var status = await GetMigrationStatus(migrationId);
            
            if (status.Status == "Completed" || status.Status == "Failed" || status.Status == "Cancelled")
            {
                return status;
            }
            
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
        
        throw new TimeoutException($"Migration {migrationId} did not complete within {timeout}");
    }
}
```

## Monitoring & Validation

### 1. Test Metrics Collection

```csharp
public class TestMetricsCollector
{
    public async Task<TestMetrics> CollectMetrics(string migrationId)
    {
        var metrics = new TestMetrics
        {
            MigrationId = migrationId,
            StartTime = await GetMigrationStartTime(migrationId),
            EndTime = await GetMigrationEndTime(migrationId),
            EntityCounts = await GetEntityCounts(migrationId),
            PerformanceMetrics = await GetPerformanceMetrics(migrationId),
            ErrorMetrics = await GetErrorMetrics(migrationId),
            ResourceUtilization = await GetResourceUtilization(migrationId)
        };
        
        return metrics;
    }
    
    public async Task ValidateDataIntegrity(string migrationId)
    {
        var sourceData = await GetSourceData(migrationId);
        var destinationData = await GetDestinationData(migrationId);
        
        // Validate entity counts
        sourceData.CategoryCount.Should().Be(destinationData.CategoryCount);
        sourceData.ProductCount.Should().Be(destinationData.ProductCount);
        sourceData.VariantCount.Should().Be(destinationData.VariantCount);
        
        // Validate data consistency
        foreach (var sourceProduct in sourceData.Products)
        {
            var destProduct = destinationData.Products
                .FirstOrDefault(p => p.SourceId == sourceProduct.Id);
            
            destProduct.Should().NotBeNull();
            destProduct.Name.Should().Be(sourceProduct.Name);
            destProduct.Price.Should().Be(sourceProduct.Price);
        }
    }
}
```

### 2. Real-Time Monitoring

```csharp
public class TestMonitor
{
    public async Task MonitorMigration(string migrationId, CancellationToken cancellationToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var status = await GetMigrationStatus(migrationId);
            var metrics = await GetRealTimeMetrics(migrationId);
            
            // Log current status
            Console.WriteLine($"Migration {migrationId}: {status.Status}");
            Console.WriteLine($"Progress: {status.PercentComplete:F1}%");
            Console.WriteLine($"Throughput: {metrics.ItemsPerSecond:F1} items/sec");
            Console.WriteLine($"Error Rate: {metrics.ErrorRate:F1}%");
            
            // Check for anomalies
            if (metrics.ErrorRate > 10)
            {
                Console.WriteLine("WARNING: High error rate detected!");
            }
            
            if (metrics.ItemsPerSecond < 0.5)
            {
                Console.WriteLine("WARNING: Low throughput detected!");
            }
            
            if (status.Status == "Completed" || status.Status == "Failed" || status.Status == "Cancelled")
            {
                break;
            }
        }
    }
}
```

## Test Automation

### 1. CI/CD Pipeline Integration

```yaml
# azure-pipelines.yml
trigger:
  branches:
    include:
      - main
      - develop

variables:
  testResourceGroup: 'bigcommerce-migration-test-rg'
  testStorageAccount: 'bcmigrationteststorage'

stages:
- stage: UnitTests
  jobs:
  - job: RunUnitTests
    steps:
    - task: DotNetCoreCLI@2
      displayName: 'Run Unit Tests'
      inputs:
        command: 'test'
        projects: '**/*UnitTests.csproj'
        arguments: '--configuration Release --collect:"XPlat Code Coverage" --logger trx --results-directory $(Agent.TempDirectory)'
    
    - task: PublishTestResults@2
      inputs:
        testResultsFormat: 'VSTest'
        testResultsFiles: '$(Agent.TempDirectory)/**/*.trx'

- stage: IntegrationTests
  dependsOn: UnitTests
  jobs:
  - job: RunIntegrationTests
    steps:
    - task: AzureResourceGroupDeployment@2
      displayName: 'Deploy Test Infrastructure'
      inputs:
        azureSubscription: 'Azure-Test-Subscription'
        resourceGroupName: '$(testResourceGroup)'
        location: 'East US'
        templateLocation: 'Linked artifact'
        csmFile: 'infrastructure/test-environment.json'
    
    - task: DotNetCoreCLI@2
      displayName: 'Run Integration Tests'
      inputs:
        command: 'test'
        projects: '**/*IntegrationTests.csproj'
        arguments: '--configuration Release --logger trx'

- stage: E2ETests
  dependsOn: IntegrationTests
  jobs:
  - job: RunE2ETests
    steps:
    - task: DotNetCoreCLI@2
      displayName: 'Run End-to-End Tests'
      inputs:
        command: 'test'
        projects: '**/*E2ETests.csproj'
        arguments: '--configuration Release --logger trx'
    
    - task: PublishTestResults@2
      inputs:
        testResultsFormat: 'VSTest'
        testResultsFiles: '**/*.trx'

- stage: PerformanceTests
  dependsOn: E2ETests
  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))
  jobs:
  - job: RunPerformanceTests
    timeoutInMinutes: 120
    steps:
    - task: DotNetCoreCLI@2
      displayName: 'Run Performance Tests'
      inputs:
        command: 'test'
        projects: '**/*PerformanceTests.csproj'
        arguments: '--configuration Release --logger trx'
```

### 2. Automated Test Reporting

```csharp
public class TestReportGenerator
{
    public async Task GenerateReport(List<TestResult> testResults)
    {
        var report = new TestReport
        {
            GeneratedAt = DateTime.UtcNow,
            TestSummary = GenerateTestSummary(testResults),
            DetailedResults = testResults,
            PerformanceMetrics = await AnalyzePerformance(testResults),
            Recommendations = GenerateRecommendations(testResults)
        };
        
        await SaveReport(report);
        await SendReportEmail(report);
    }
    
    private TestSummary GenerateTestSummary(List<TestResult> results)
    {
        return new TestSummary
        {
            TotalTests = results.Count,
            PassedTests = results.Count(r => r.Status == "Passed"),
            FailedTests = results.Count(r => r.Status == "Failed"),
            SkippedTests = results.Count(r => r.Status == "Skipped"),
            TotalExecutionTime = results.Sum(r => r.ExecutionTime.TotalMilliseconds),
            SuccessRate = (double)results.Count(r => r.Status == "Passed") / results.Count * 100
        };
    }
}
```

## Testing Tools & Frameworks

### Recommended Testing Stack

**Unit Testing:**
- xUnit.net
- FluentAssertions
- Moq
- AutoFixture

**Integration Testing:**
- Azure Functions Test Framework
- Testcontainers.NET
- WireMock.NET (for API mocking)

**End-to-End Testing:**
- Microsoft.AspNetCore.Mvc.Testing
- Selenium WebDriver (for UI testing)
- RestSharp (for API testing)

**Performance Testing:**
- NBomber
- Azure Load Testing
- Application Insights

**Chaos Testing:**
- Chaos Toolkit
- Gremlin
- Azure Chaos Studio

### Test Configuration

```json
{
  "TestSettings": {
    "Environment": "Development",
    "BigCommerceAPI": {
      "BaseUrl": "https://api.bigcommerce.com",
      "TestStoreHash": "test-store-123",
      "MockResponses": true
    },
    "AzureStorage": {
      "ConnectionString": "UseDevelopmentStorage=true",
      "UseEmulator": true
    },
    "OpenSearch": {
      "Endpoint": "http://localhost:9200",
      "UseLocalInstance": true
    },
    "TestData": {
      "MaxProducts": 1000,
      "MaxVariantsPerProduct": 50,
      "MaxImagesPerProduct": 20,
      "GenerateTestData": true
    },
    "Performance": {
      "MaxExecutionTime": "00:30:00",
      "ExpectedThroughput": 100,
      "MaxErrorRate": 0.05
    }
  }
}
```

This comprehensive testing strategy ensures that all workflows are thoroughly validated, from individual components to complete end-to-end scenarios, including complex product processing, granular cancellation, and performance under load. 