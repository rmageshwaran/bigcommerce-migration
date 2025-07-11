# Integration and E2E Testing Strategy

## BigCommerce Migration System - Advanced Testing Approach

---

**Document Version:** 1.0  
**Created:** January 2025  
**Purpose:** Comprehensive integration and end-to-end testing strategy and thought process  
**Scope:** Testing strategy for BigCommerce Migration System

---

## 🎯 **Testing Philosophy and Approach**

### **Testing Pyramid for Migration System**

```
              E2E Tests (10%)
           ┌─────────────────────┐
           │ Complete Workflows  │
           │ Real-world Scenarios│
           │ Performance Testing │
           └─────────────────────┘
                      ▲
         Integration Tests (20%)
      ┌─────────────────────────────┐
      │ Service Interactions        │
      │ External API Integration    │
      │ Database Operations         │
      │ Configuration Testing       │
      └─────────────────────────────┘
                      ▲
           Unit Tests (70%)
   ┌─────────────────────────────────────┐
   │ Individual Method Testing           │
   │ Business Logic Validation           │
   │ Error Handling                      │
   │ Edge Cases                          │
   └─────────────────────────────────────┘
```

### **Core Testing Principles**

1. **Fail Fast**: Tests should identify issues as early as possible
2. **Realistic Scenarios**: Tests should mirror real-world usage patterns
3. **Data Integrity**: Tests should validate data consistency across migrations
4. **Performance Validation**: Tests should ensure system performance under load
5. **Resilience Testing**: Tests should validate system behavior during failures

---

## 🔧 **Integration Tests (20% Coverage)**

### **Integration Test Categories**

#### **1. External Service Integration Tests**

**Thought Process:**
- Test actual connections to BigCommerce API
- Validate authentication and authorization
- Test rate limiting and throttling behavior
- Verify data transformation accuracy
- Test error handling for various API responses

```csharp
[TestFixture]
public class BigCommerceApiIntegrationTests
{
    private BigCommerceApiClient _apiClient;
    private TestConfiguration _testConfig;
    
    [SetUp]
    public void Setup()
    {
        _testConfig = TestConfiguration.Load();
        _apiClient = new BigCommerceApiClient(_testConfig.BigCommerceConfig);
    }
    
    [Test]
    public async Task GetProducts_WithValidAuthentication_ReturnsProducts()
    {
        // Arrange
        var storeId = _testConfig.TestStoreId;
        
        // Act
        var result = await _apiClient.GetProductsAsync(storeId, new ProductFilter 
        { 
            Limit = 10 
        });
        
        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Data, Is.Not.Null);
        Assert.That(result.Data.Count(), Is.LessThanOrEqualTo(10));
        
        // Validate data structure
        var firstProduct = result.Data.FirstOrDefault();
        if (firstProduct != null)
        {
            Assert.That(firstProduct.Id, Is.Not.Null);
            Assert.That(firstProduct.Name, Is.Not.Null);
            Assert.That(firstProduct.Price, Is.GreaterThan(0));
        }
    }
    
    [Test]
    public async Task CreateProduct_WithValidProduct_ReturnsCreatedProduct()
    {
        // Arrange
        var testProduct = new BigCommerceProduct
        {
            Name = $"Integration Test Product {DateTime.UtcNow:yyyyMMddHHmmss}",
            Type = "physical",
            Price = 29.99m,
            Categories = new[] { 1 }, // Assuming category 1 exists
            Weight = 1.0m,
            Description = "Test product created during integration testing"
        };
        
        // Act
        var createResult = await _apiClient.CreateProductAsync(_testConfig.TestStoreId, testProduct);
        
        // Assert
        Assert.That(createResult.IsSuccess, Is.True);
        Assert.That(createResult.Data.Id, Is.Not.Null);
        Assert.That(createResult.Data.Name, Is.EqualTo(testProduct.Name));
        
        // Cleanup
        await _apiClient.DeleteProductAsync(_testConfig.TestStoreId, createResult.Data.Id);
    }
    
    [Test]
    public async Task GetProducts_WithRateLimiting_HandlesThrottling()
    {
        // Arrange
        var requests = new List<Task<ApiResponse<IEnumerable<BigCommerceProduct>>>>();
        
        // Act - Make multiple concurrent requests to test rate limiting
        for (int i = 0; i < 20; i++)
        {
            requests.Add(_apiClient.GetProductsAsync(_testConfig.TestStoreId, new ProductFilter { Limit = 1 }));
        }
        
        var results = await Task.WhenAll(requests);
        
        // Assert
        var successCount = results.Count(r => r.IsSuccess);
        var rateLimitedCount = results.Count(r => r.StatusCode == 429);
        
        Assert.That(successCount, Is.GreaterThan(0), "At least some requests should succeed");
        
        // If rate limited, verify proper handling
        if (rateLimitedCount > 0)
        {
            var rateLimitedResults = results.Where(r => r.StatusCode == 429);
            foreach (var result in rateLimitedResults)
            {
                Assert.That(result.RetryAfter, Is.Not.Null, "Rate limited responses should include retry-after");
            }
        }
    }
}
```

#### **2. Database Integration Tests**

**Thought Process:**
- Test actual database operations
- Validate data persistence and retrieval
- Test transaction handling
- Verify connection pooling behavior
- Test database migrations and schema changes

```csharp
[TestFixture]
public class MigrationRepositoryIntegrationTests
{
    private MigrationRepository _repository;
    private IDbConnectionFactory _connectionFactory;
    private string _testDatabaseConnectionString;
    
    [SetUp]
    public void Setup()
    {
        _testDatabaseConnectionString = TestConfiguration.Load().TestDatabaseConnectionString;
        _connectionFactory = new DbConnectionFactory(_testDatabaseConnectionString);
        _repository = new MigrationRepository(_connectionFactory);
        
        // Ensure clean database state
        CleanupTestData();
    }
    
    [Test]
    public async Task SaveMigrationStatus_WithValidData_PersistsCorrectly()
    {
        // Arrange
        var migrationStatus = new MigrationStatus
        {
            MigrationId = Guid.NewGuid().ToString(),
            SourceStoreId = "source-123",
            DestinationStoreId = "dest-456",
            Status = MigrationStatusEnum.InProgress,
            StartTime = DateTime.UtcNow,
            TotalEntities = 1000,
            ProcessedEntities = 250,
            CreatedBy = "integration-test-user"
        };
        
        // Act
        var saveResult = await _repository.SaveMigrationStatusAsync(migrationStatus);
        
        // Assert
        Assert.That(saveResult, Is.True);
        
        // Verify data was persisted correctly
        var retrievedStatus = await _repository.GetMigrationStatusAsync(migrationStatus.MigrationId);
        Assert.That(retrievedStatus, Is.Not.Null);
        Assert.That(retrievedStatus.MigrationId, Is.EqualTo(migrationStatus.MigrationId));
        Assert.That(retrievedStatus.Status, Is.EqualTo(MigrationStatusEnum.InProgress));
        Assert.That(retrievedStatus.TotalEntities, Is.EqualTo(1000));
        Assert.That(retrievedStatus.ProcessedEntities, Is.EqualTo(250));
    }
    
    [Test]
    public async Task GetMigrationHistory_WithMultipleEntries_ReturnsOrderedResults()
    {
        // Arrange
        var migrationStatuses = new List<MigrationStatus>();
        for (int i = 0; i < 5; i++)
        {
            var status = new MigrationStatus
            {
                MigrationId = Guid.NewGuid().ToString(),
                SourceStoreId = "source-123",
                DestinationStoreId = "dest-456",
                Status = MigrationStatusEnum.Completed,
                StartTime = DateTime.UtcNow.AddHours(-i),
                EndTime = DateTime.UtcNow.AddHours(-i).AddMinutes(30),
                CreatedBy = "integration-test-user"
            };
            
            migrationStatuses.Add(status);
            await _repository.SaveMigrationStatusAsync(status);
        }
        
        // Act
        var history = await _repository.GetMigrationHistoryAsync("source-123", 10);
        
        // Assert
        Assert.That(history.Count(), Is.EqualTo(5));
        
        // Verify ordering (most recent first)
        var orderedHistory = history.ToList();
        for (int i = 0; i < orderedHistory.Count - 1; i++)
        {
            Assert.That(orderedHistory[i].StartTime, Is.GreaterThan(orderedHistory[i + 1].StartTime));
        }
    }
    
    [Test]
    public async Task Transaction_WithRollback_DoesNotPersistData()
    {
        // Arrange
        var migrationId = Guid.NewGuid().ToString();
        var migrationStatus = new MigrationStatus
        {
            MigrationId = migrationId,
            SourceStoreId = "source-123",
            DestinationStoreId = "dest-456",
            Status = MigrationStatusEnum.InProgress,
            StartTime = DateTime.UtcNow,
            CreatedBy = "integration-test-user"
        };
        
        // Act & Assert
        try
        {
            using var transaction = await _repository.BeginTransactionAsync();
            await _repository.SaveMigrationStatusAsync(migrationStatus);
            
            // Simulate an error that causes rollback
            throw new InvalidOperationException("Simulated error");
        }
        catch (InvalidOperationException)
        {
            // Expected exception
        }
        
        // Verify data was not persisted due to rollback
        var retrievedStatus = await _repository.GetMigrationStatusAsync(migrationId);
        Assert.That(retrievedStatus, Is.Null);
    }
    
    [TearDown]
    public void TearDown()
    {
        CleanupTestData();
    }
    
    private void CleanupTestData()
    {
        // Clean up test data to ensure test isolation
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MigrationStatus WHERE CreatedBy = 'integration-test-user'";
        command.ExecuteNonQuery();
    }
}
```

#### **3. Service Layer Integration Tests**

**Thought Process:**
- Test service interactions and dependencies
- Validate dependency injection configuration
- Test cross-cutting concerns (logging, caching, etc.)
- Verify service composition and orchestration

```csharp
[TestFixture]
public class MigrationServiceIntegrationTests
{
    private ServiceProvider _serviceProvider;
    private IMigrationService _migrationService;
    private TestConfiguration _testConfig;
    
    [SetUp]
    public void Setup()
    {
        _testConfig = TestConfiguration.Load();
        
        // Configure services as they would be in production
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        
        _migrationService = _serviceProvider.GetRequiredService<IMigrationService>();
    }
    
    [Test]
    public async Task InitializeMigration_WithValidRequest_ConfiguresAllServices()
    {
        // Arrange
        var request = new MigrationRequest
        {
            MigrationId = Guid.NewGuid().ToString(),
            SourceStoreId = _testConfig.TestSourceStoreId,
            DestinationStoreId = _testConfig.TestDestinationStoreId,
            Entities = new[] { "Products", "Categories" }
        };
        
        // Act
        var result = await _migrationService.InitializeMigrationAsync(request);
        
        // Assert
        Assert.That(result.IsSuccess, Is.True);
        
        // Verify all required services were configured
        var dependencyResolver = _serviceProvider.GetRequiredService<IEntityDependencyResolver>();
        var resolvedEntities = await dependencyResolver.ResolveEntitiesAsync(request.Entities);
        
        Assert.That(resolvedEntities.Count(), Is.GreaterThan(request.Entities.Length));
        Assert.That(resolvedEntities, Contains.Item("Categories"));
        Assert.That(resolvedEntities, Contains.Item("Products"));
    }
    
    [Test]
    public async Task ProcessMigration_WithRealData_HandlesCompleteWorkflow()
    {
        // Arrange
        var request = new MigrationRequest
        {
            MigrationId = Guid.NewGuid().ToString(),
            SourceStoreId = _testConfig.TestSourceStoreId,
            DestinationStoreId = _testConfig.TestDestinationStoreId,
            Entities = new[] { "Categories" } // Start with simpler entity
        };
        
        // Act
        var result = await _migrationService.ProcessMigrationAsync(request);
        
        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.ProcessedEntities, Is.GreaterThan(0));
        
        // Verify data was actually migrated
        var apiClient = _serviceProvider.GetRequiredService<IBigCommerceApiClient>();
        var categoriesInDestination = await apiClient.GetCategoriesAsync(_testConfig.TestDestinationStoreId);
        
        Assert.That(categoriesInDestination.IsSuccess, Is.True);
        Assert.That(categoriesInDestination.Data.Count(), Is.GreaterThan(0));
    }
    
    private void ConfigureServices(ServiceCollection services)
    {
        // Configure all services as they would be in production
        services.AddScoped<IMigrationService, MigrationService>();
        services.AddScoped<IEntityDependencyResolver, EntityDependencyResolver>();
        services.AddScoped<IBigCommerceApiClient, BigCommerceApiClient>();
        services.AddScoped<ITransformationService, TransformationService>();
        
        // Add configuration
        services.AddSingleton<IConfiguration>(provider =>
        {
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.test.json");
            return builder.Build();
        });
        
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }
    
    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
    }
}
```

---

## 🌐 **End-to-End Tests (10% Coverage)**

### **E2E Test Categories**

#### **1. Complete Migration Workflow Tests**

**Thought Process:**
- Test the entire migration process from start to finish
- Validate real-world scenarios with actual data
- Test system behavior under various conditions
- Verify data integrity across the complete pipeline

```csharp
[TestFixture]
public class CompleteMigrationWorkflowTests
{
    private WebApplicationFactory<Program> _factory;
    private HttpClient _httpClient;
    private TestConfiguration _testConfig;
    
    [SetUp]
    public void Setup()
    {
        _testConfig = TestConfiguration.Load();
        
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddJsonFile("appsettings.test.json");
                });
                
                builder.ConfigureServices(services =>
                {
                    // Override services for testing
                    services.AddScoped<INotificationService, TestNotificationService>();
                });
            });
        
        _httpClient = _factory.CreateClient();
    }
    
    [Test]
    public async Task CompleteMigration_SmallStore_CompletesSuccessfully()
    {
        // Arrange
        var migrationRequest = new MigrationRequest
        {
            MigrationId = Guid.NewGuid().ToString(),
            SourceStoreId = _testConfig.SmallTestStoreId,
            DestinationStoreId = _testConfig.EmptyTestStoreId,
            Entities = new[] { "Categories", "Brands", "Products" }
        };
        
        // Act - Start migration
        var startResponse = await _httpClient.PostAsJsonAsync("/api/migration/start", migrationRequest);
        startResponse.EnsureSuccessStatusCode();
        
        var startResult = await startResponse.Content.ReadFromJsonAsync<MigrationStartResult>();
        Assert.That(startResult.IsSuccess, Is.True);
        
        // Monitor migration progress
        var migrationId = startResult.MigrationId;
        var completed = false;
        var maxWaitTime = TimeSpan.FromMinutes(30);
        var startTime = DateTime.UtcNow;
        
        while (!completed && DateTime.UtcNow - startTime < maxWaitTime)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            
            var statusResponse = await _httpClient.GetAsync($"/api/migration/{migrationId}/status");
            statusResponse.EnsureSuccessStatusCode();
            
            var status = await statusResponse.Content.ReadFromJsonAsync<MigrationStatus>();
            
            if (status.Status == MigrationStatusEnum.Completed)
            {
                completed = true;
                
                // Assert final results
                Assert.That(status.IsSuccess, Is.True);
                Assert.That(status.ProcessedEntities, Is.GreaterThan(0));
                Assert.That(status.FailedEntities, Is.EqualTo(0));
                
                // Verify data integrity
                await ValidateDataIntegrity(migrationRequest.SourceStoreId, migrationRequest.DestinationStoreId);
            }
            else if (status.Status == MigrationStatusEnum.Failed)
            {
                Assert.Fail($"Migration failed: {status.ErrorMessage}");
            }
        }
        
        Assert.That(completed, Is.True, "Migration should complete within expected time");
    }
    
    [Test]
    public async Task CompleteMigration_WithCancellation_StopsGracefully()
    {
        // Arrange
        var migrationRequest = new MigrationRequest
        {
            MigrationId = Guid.NewGuid().ToString(),
            SourceStoreId = _testConfig.LargeTestStoreId,
            DestinationStoreId = _testConfig.EmptyTestStoreId,
            Entities = new[] { "Products" }
        };
        
        // Act - Start migration
        var startResponse = await _httpClient.PostAsJsonAsync("/api/migration/start", migrationRequest);
        startResponse.EnsureSuccessStatusCode();
        
        var startResult = await startResponse.Content.ReadFromJsonAsync<MigrationStartResult>();
        var migrationId = startResult.MigrationId;
        
        // Wait for migration to start processing
        await Task.Delay(TimeSpan.FromSeconds(30));
        
        // Cancel migration
        var cancelResponse = await _httpClient.PostAsync($"/api/migration/{migrationId}/cancel", null);
        cancelResponse.EnsureSuccessStatusCode();
        
        // Wait for cancellation to complete
        await Task.Delay(TimeSpan.FromSeconds(10));
        
        // Assert
        var statusResponse = await _httpClient.GetAsync($"/api/migration/{migrationId}/status");
        statusResponse.EnsureSuccessStatusCode();
        
        var finalStatus = await statusResponse.Content.ReadFromJsonAsync<MigrationStatus>();
        Assert.That(finalStatus.Status, Is.EqualTo(MigrationStatusEnum.Cancelled));
        Assert.That(finalStatus.ProcessedEntities, Is.GreaterThan(0)); // Some entities should have been processed
    }
    
    private async Task ValidateDataIntegrity(string sourceStoreId, string destinationStoreId)
    {
        var apiClient = _factory.Services.GetRequiredService<IBigCommerceApiClient>();
        
        // Validate categories
        var sourceCategories = await apiClient.GetCategoriesAsync(sourceStoreId);
        var destCategories = await apiClient.GetCategoriesAsync(destinationStoreId);
        
        Assert.That(sourceCategories.IsSuccess, Is.True);
        Assert.That(destCategories.IsSuccess, Is.True);
        Assert.That(destCategories.Data.Count(), Is.EqualTo(sourceCategories.Data.Count()));
        
        // Validate products
        var sourceProducts = await apiClient.GetProductsAsync(sourceStoreId, new ProductFilter { Limit = 50 });
        var destProducts = await apiClient.GetProductsAsync(destinationStoreId, new ProductFilter { Limit = 50 });
        
        Assert.That(sourceProducts.IsSuccess, Is.True);
        Assert.That(destProducts.IsSuccess, Is.True);
        Assert.That(destProducts.Data.Count(), Is.EqualTo(sourceProducts.Data.Count()));
        
        // Validate specific product data
        var sourceProduct = sourceProducts.Data.First();
        var destProduct = destProducts.Data.FirstOrDefault(p => p.Name == sourceProduct.Name);
        
        Assert.That(destProduct, Is.Not.Null);
        Assert.That(destProduct.Price, Is.EqualTo(sourceProduct.Price));
        Assert.That(destProduct.Weight, Is.EqualTo(sourceProduct.Weight));
    }
}
```

#### **2. Performance and Load Testing**

**Thought Process:**
- Test system performance under realistic load
- Validate system behavior with large datasets
- Test resource utilization and scalability
- Verify SLA compliance

```csharp
[TestFixture]
public class MigrationPerformanceTests
{
    private WebApplicationFactory<Program> _factory;
    private HttpClient _httpClient;
    private TestConfiguration _testConfig;
    
    [SetUp]
    public void Setup()
    {
        _testConfig = TestConfiguration.Load();
        _factory = new WebApplicationFactory<Program>();
        _httpClient = _factory.CreateClient();
    }
    
    [Test]
    public async Task LargeStoreMigration_10000Products_CompletesWithinSLA()
    {
        // Arrange
        var migrationRequest = new MigrationRequest
        {
            MigrationId = Guid.NewGuid().ToString(),
            SourceStoreId = _testConfig.LargeTestStoreId, // Store with 10,000 products
            DestinationStoreId = _testConfig.EmptyTestStoreId,
            Entities = new[] { "Products" }
        };
        
        var startTime = DateTime.UtcNow;
        
        // Act
        var startResponse = await _httpClient.PostAsJsonAsync("/api/migration/start", migrationRequest);
        startResponse.EnsureSuccessStatusCode();
        
        var startResult = await startResponse.Content.ReadFromJsonAsync<MigrationStartResult>();
        var migrationId = startResult.MigrationId;
        
        // Monitor migration
        var completed = false;
        var maxWaitTime = TimeSpan.FromHours(8); // SLA: 8 hours for 10K products
        
        while (!completed && DateTime.UtcNow - startTime < maxWaitTime)
        {
            await Task.Delay(TimeSpan.FromMinutes(2));
            
            var statusResponse = await _httpClient.GetAsync($"/api/migration/{migrationId}/status");
            statusResponse.EnsureSuccessStatusCode();
            
            var status = await statusResponse.Content.ReadFromJsonAsync<MigrationStatus>();
            
            if (status.Status == MigrationStatusEnum.Completed)
            {
                completed = true;
                var duration = DateTime.UtcNow - startTime;
                
                // Assert performance SLA
                Assert.That(duration, Is.LessThan(TimeSpan.FromHours(8)));
                Assert.That(status.ProcessedEntities, Is.EqualTo(10000));
                Assert.That(status.FailedEntities, Is.LessThan(50)); // <0.5% failure rate
                
                // Calculate throughput
                var throughputPerHour = status.ProcessedEntities / duration.TotalHours;
                Assert.That(throughputPerHour, Is.GreaterThan(1250)); // >1,250 products/hour
            }
        }
        
        Assert.That(completed, Is.True, "Large store migration should complete within SLA");
    }
    
    [Test]
    public async Task ConcurrentMigrations_MultipleStores_HandlesLoad()
    {
        // Arrange
        var migrationRequests = new List<MigrationRequest>();
        for (int i = 0; i < 5; i++)
        {
            migrationRequests.Add(new MigrationRequest
            {
                MigrationId = Guid.NewGuid().ToString(),
                SourceStoreId = _testConfig.MediumTestStoreIds[i],
                DestinationStoreId = _testConfig.EmptyTestStoreIds[i],
                Entities = new[] { "Categories", "Products" }
            });
        }
        
        // Act - Start all migrations concurrently
        var startTasks = migrationRequests.Select(async request =>
        {
            var response = await _httpClient.PostAsJsonAsync("/api/migration/start", request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<MigrationStartResult>();
        });
        
        var startResults = await Task.WhenAll(startTasks);
        
        // Monitor all migrations
        var allCompleted = false;
        var maxWaitTime = TimeSpan.FromHours(4);
        var startTime = DateTime.UtcNow;
        
        while (!allCompleted && DateTime.UtcNow - startTime < maxWaitTime)
        {
            await Task.Delay(TimeSpan.FromMinutes(1));
            
            var statusTasks = startResults.Select(async result =>
            {
                var response = await _httpClient.GetAsync($"/api/migration/{result.MigrationId}/status");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<MigrationStatus>();
            });
            
            var statuses = await Task.WhenAll(statusTasks);
            
            allCompleted = statuses.All(s => s.Status == MigrationStatusEnum.Completed || s.Status == MigrationStatusEnum.Failed);
        }
        
        // Assert
        Assert.That(allCompleted, Is.True, "All concurrent migrations should complete");
        
        // Verify system handled the load well
        var finalStatusTasks = startResults.Select(async result =>
        {
            var response = await _httpClient.GetAsync($"/api/migration/{result.MigrationId}/status");
            return await response.Content.ReadFromJsonAsync<MigrationStatus>();
        });
        
        var finalStatuses = await Task.WhenAll(finalStatusTasks);
        var successfulMigrations = finalStatuses.Count(s => s.Status == MigrationStatusEnum.Completed);
        
        Assert.That(successfulMigrations, Is.EqualTo(5), "All migrations should complete successfully");
    }
}
```

#### **3. Failure and Recovery Testing**

**Thought Process:**
- Test system resilience under various failure scenarios
- Validate recovery mechanisms
- Test data consistency during failures
- Verify system state after recovery

```csharp
[TestFixture]
public class MigrationResilienceTests
{
    private WebApplicationFactory<Program> _factory;
    private HttpClient _httpClient;
    private TestConfiguration _testConfig;
    
    [Test]
    public async Task Migration_WithNetworkInterruption_RecoversAutomatically()
    {
        // Arrange
        var migrationRequest = new MigrationRequest
        {
            MigrationId = Guid.NewGuid().ToString(),
            SourceStoreId = _testConfig.TestSourceStoreId,
            DestinationStoreId = _testConfig.TestDestinationStoreId,
            Entities = new[] { "Products" }
        };
        
        // Act - Start migration
        var startResponse = await _httpClient.PostAsJsonAsync("/api/migration/start", migrationRequest);
        startResponse.EnsureSuccessStatusCode();
        
        var startResult = await startResponse.Content.ReadFromJsonAsync<MigrationStartResult>();
        var migrationId = startResult.MigrationId;
        
        // Wait for migration to start processing
        await Task.Delay(TimeSpan.FromSeconds(30));
        
        // Simulate network interruption by stopping the test server
        _factory.Dispose();
        
        // Wait for interruption period
        await Task.Delay(TimeSpan.FromSeconds(60));
        
        // Restart the system
        _factory = new WebApplicationFactory<Program>();
        _httpClient = _factory.CreateClient();
        
        // Verify migration can be resumed
        var resumeResponse = await _httpClient.PostAsync($"/api/migration/{migrationId}/resume", null);
        resumeResponse.EnsureSuccessStatusCode();
        
        // Monitor completion
        var completed = false;
        var maxWaitTime = TimeSpan.FromMinutes(30);
        var startTime = DateTime.UtcNow;
        
        while (!completed && DateTime.UtcNow - startTime < maxWaitTime)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            
            var statusResponse = await _httpClient.GetAsync($"/api/migration/{migrationId}/status");
            statusResponse.EnsureSuccessStatusCode();
            
            var status = await statusResponse.Content.ReadFromJsonAsync<MigrationStatus>();
            
            if (status.Status == MigrationStatusEnum.Completed)
            {
                completed = true;
                Assert.That(status.IsSuccess, Is.True);
                Assert.That(status.ProcessedEntities, Is.GreaterThan(0));
            }
        }
        
        Assert.That(completed, Is.True, "Migration should complete after recovery");
    }
}
```

---

## 📊 **Test Environment Strategy**

### **Test Data Management**

#### **Test Store Configuration**
```csharp
public class TestConfiguration
{
    public string SmallTestStoreId { get; set; } // ~100 products
    public string MediumTestStoreId { get; set; } // ~1,000 products
    public string LargeTestStoreId { get; set; } // ~10,000 products
    public string EmptyTestStoreId { get; set; } // Clean destination
    
    public string[] MediumTestStoreIds { get; set; } // For concurrent testing
    public string[] EmptyTestStoreIds { get; set; } // For concurrent testing
    
    public BigCommerceConfiguration BigCommerceConfig { get; set; }
    public string TestDatabaseConnectionString { get; set; }
    
    public static TestConfiguration Load()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.test.json")
            .AddEnvironmentVariables()
            .Build();
            
        return configuration.GetSection("TestConfiguration").Get<TestConfiguration>();
    }
}
```

#### **Test Data Cleanup Strategy**
```csharp
public class TestDataCleanupService
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<TestDataCleanupService> _logger;
    
    public async Task CleanupTestStoreAsync(string storeId)
    {
        try
        {
            // Delete test products
            var products = await _apiClient.GetProductsAsync(storeId, new ProductFilter());
            foreach (var product in products.Data.Where(p => p.Name.Contains("Test") || p.Name.Contains("Integration")))
            {
                await _apiClient.DeleteProductAsync(storeId, product.Id);
            }
            
            // Delete test categories
            var categories = await _apiClient.GetCategoriesAsync(storeId);
            foreach (var category in categories.Data.Where(c => c.Name.Contains("Test") || c.Name.Contains("Integration")))
            {
                await _apiClient.DeleteCategoryAsync(storeId, category.Id);
            }
            
            _logger.LogInformation("Cleaned up test data for store {StoreId}", storeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up test data for store {StoreId}", storeId);
        }
    }
}
```

### **CI/CD Integration**

#### **Azure DevOps Pipeline for E2E Tests**
```yaml
# e2e-tests-pipeline.yml
trigger:
  - main
  - develop

pool:
  vmImage: 'ubuntu-latest'

stages:
- stage: SetupTestEnvironment
  jobs:
  - job: PrepareTestStores
    steps:
    - task: AzureKeyVault@2
      inputs:
        azureSubscription: 'Test-Subscription'
        KeyVaultName: 'test-keyvault'
        SecretsFilter: 'bigcommerce-test-*'
    
    - task: PowerShell@2
      inputs:
        targetType: 'inline'
        script: |
          # Setup test stores with required data
          ./scripts/setup-test-stores.ps1

- stage: RunE2ETests
  dependsOn: SetupTestEnvironment
  jobs:
  - job: ExecuteE2ETests
    timeoutInMinutes: 240 # 4 hours for E2E tests
    steps:
    - task: DotNetCoreCLI@2
      displayName: 'Run E2E Tests'
      inputs:
        command: 'test'
        projects: '**/*E2ETests.csproj'
        arguments: '--configuration Release --logger trx --collect:"XPlat Code Coverage"'
    
    - task: PublishTestResults@2
      inputs:
        testResultsFormat: 'VSTest'
        testResultsFiles: '**/*.trx'
        failTaskOnFailedTests: true

- stage: Cleanup
  dependsOn: RunE2ETests
  condition: always()
  jobs:
  - job: CleanupTestData
    steps:
    - task: PowerShell@2
      inputs:
        targetType: 'inline'
        script: |
          # Cleanup test stores
          ./scripts/cleanup-test-stores.ps1
```

---

## 🎯 **Summary: Integration vs E2E Testing Approach**

| **Aspect** | **Integration Tests** | **E2E Tests** |
|------------|----------------------|---------------|
| **Scope** | Service interactions, APIs, databases | Complete user workflows |
| **Duration** | 5-30 minutes per test | 30 minutes - 4 hours per test |
| **Frequency** | Every pull request | Nightly builds, releases |
| **Environment** | Controlled test environment | Production-like environment |
| **Data** | Minimal test data | Realistic datasets |
| **Focus** | Technical integration | Business scenarios |

### **Key Success Metrics**

- **Integration Tests**: 95% pass rate, < 30 minutes total execution
- **E2E Tests**: 90% pass rate, < 4 hours total execution
- **Coverage**: Combined 30% of total test coverage
- **Reliability**: Flaky test rate < 2%
- **Maintenance**: Test maintenance time < 20% of development time

This comprehensive approach ensures our BigCommerce Migration System is thoroughly tested at all levels, providing confidence in both technical implementation and business value delivery.

---

**Document Status**: Final  
**Review Cycle**: Monthly  
**Next Review**: February 2025 