# Azure Durable Functions Deterministic Behavior Guide

## 🎯 **Critical Requirement**

**Azure Durable Functions MUST be deterministic** - they must produce the same result every time they're replayed. This is essential for:
- **Reliability**: Orchestrators can restart from any point
- **Fault Tolerance**: Automatic recovery from failures
- **Cost Optimization**: Efficient checkpoint and replay system

## 🚨 **Common Violations & Solutions**

### **1. DateTime Operations**

❌ **NEVER USE:**
```csharp
// ❌ Non-deterministic - different value on each replay
var now = DateTime.UtcNow;
var endTime = DateTime.Now;
```

✅ **ALWAYS USE:**
```csharp
// ✅ Deterministic - same value on each replay
var now = context.CurrentUtcDateTime;
var endTime = context.CurrentUtcDateTime;
```

### **2. GUID Generation**

❌ **NEVER USE:**
```csharp
// ❌ Non-deterministic - different GUID on each replay
var correlationId = Guid.NewGuid();
```

✅ **ALWAYS USE:**
```csharp
// ✅ Deterministic - same GUID on each replay
var correlationId = context.NewGuid();
```

### **3. Delays & Timing**

❌ **NEVER USE:**
```csharp
// ❌ Non-deterministic - timing issues on replay
await Task.Delay(TimeSpan.FromSeconds(30));
Thread.Sleep(1000);
```

✅ **ALWAYS USE:**
```csharp
// ✅ Deterministic - proper timer on replay
await context.CreateTimer(context.CurrentUtcDateTime.Add(TimeSpan.FromSeconds(30)), CancellationToken.None);
```

### **4. HTTP Calls & External I/O**

❌ **NEVER USE:**
```csharp
// ❌ Non-deterministic - external calls in orchestrator
using var client = new HttpClient();
var response = await client.GetAsync("https://api.example.com/data");
```

✅ **ALWAYS USE:**
```csharp
// ✅ Deterministic - external calls in activity functions
var response = await context.CallActivityAsync<string>("GetExternalData", "https://api.example.com/data");
```

### **5. ConfigureAwait(false)**

❌ **NEVER USE:**
```csharp
// ❌ Can cause non-deterministic behavior
await someTask.ConfigureAwait(false);
```

✅ **ALWAYS USE:**
```csharp
// ✅ Use default ConfigureAwait behavior
await someTask;
```

### **6. Random Numbers**

❌ **NEVER USE:**
```csharp
// ❌ Non-deterministic - different values on replay
var random = new Random();
var value = random.Next(1, 100);
```

✅ **ALWAYS USE:**
```csharp
// ✅ Deterministic - random generation in activity
var value = await context.CallActivityAsync<int>("GenerateRandomNumber", new { Min = 1, Max = 100 });
```

## 🏗️ **Orchestrator Template**

Use this template for new orchestrator functions:

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.DurableTask;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Functions.Orchestrators;

public class MyOrchestratorFunction
{
    private readonly ILogger<MyOrchestratorFunction> _logger;

    public MyOrchestratorFunction(ILogger<MyOrchestratorFunction> logger)
    {
        _logger = logger;
    }

    [Function("MyOrchestrator")]
    public async Task<MyResult> RunOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context,
        MyRequest request)
    {
        var logger = context.CreateReplaySafeLogger(_logger);
        
        try
        {
            // ✅ Use deterministic operations only
            var startTime = context.CurrentUtcDateTime;
            var correlationId = context.NewGuid();
            
            logger.LogInformation("Starting orchestration {CorrelationId} at {StartTime}", 
                correlationId, startTime);

            // ✅ All I/O operations must be in activity functions
            var step1Result = await context.CallActivityAsync<Step1Result>("Step1Activity", request.Step1Input);
            
            // ✅ Use deterministic conditions
            if (step1Result.IsSuccess)
            {
                var step2Result = await context.CallActivityAsync<Step2Result>("Step2Activity", step1Result.Data);
                
                // ✅ Use context.CreateTimer for delays
                if (step2Result.RequiresDelay)
                {
                    await context.CreateTimer(
                        context.CurrentUtcDateTime.Add(TimeSpan.FromMinutes(5)), 
                        CancellationToken.None);
                }
                
                return new MyResult
                {
                    CorrelationId = correlationId,
                    StartTime = startTime,
                    EndTime = context.CurrentUtcDateTime,
                    IsSuccess = true,
                    Data = step2Result.Data
                };
            }
            else
            {
                logger.LogWarning("Step1 failed: {Error}", step1Result.Error);
                return new MyResult
                {
                    CorrelationId = correlationId,
                    StartTime = startTime,
                    EndTime = context.CurrentUtcDateTime,
                    IsSuccess = false,
                    Error = step1Result.Error
                };
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Orchestration failed");
            throw;
        }
    }
}
```

## 🔧 **Activity Function Template**

All I/O operations should be in activity functions:

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Functions.Activities;

public class MyActivityFunction
{
    private readonly ILogger<MyActivityFunction> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public MyActivityFunction(ILogger<MyActivityFunction> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    [Function("Step1Activity")]
    public async Task<Step1Result> ExecuteStep1([ActivityTrigger] Step1Input input)
    {
        try
        {
            // ✅ Non-deterministic operations are allowed in activity functions
            using var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync(input.ApiUrl);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return new Step1Result
                {
                    IsSuccess = true,
                    Data = content
                };
            }
            else
            {
                return new Step1Result
                {
                    IsSuccess = false,
                    Error = $"HTTP {response.StatusCode}: {response.ReasonPhrase}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step1Activity failed");
            return new Step1Result
            {
                IsSuccess = false,
                Error = ex.Message
            };
        }
    }
}
```

## 🧪 **Testing Deterministic Behavior**

### **Unit Test Template**

```csharp
[Test]
public async Task MyOrchestrator_DeterministicBehavior_ProducesSameResult()
{
    // Arrange
    var mockContext = new Mock<TaskOrchestrationContext>();
    var fixedDateTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    var fixedGuid = Guid.Parse("12345678-1234-1234-1234-123456789012");
    
    mockContext.Setup(x => x.CurrentUtcDateTime).Returns(fixedDateTime);
    mockContext.Setup(x => x.NewGuid()).Returns(fixedGuid);
    
    var orchestrator = new MyOrchestratorFunction();
    var request = new MyRequest { /* test data */ };
    
    // Act - Run multiple times to verify determinism
    var result1 = await orchestrator.RunOrchestrator(mockContext.Object, request);
    var result2 = await orchestrator.RunOrchestrator(mockContext.Object, request);
    
    // Assert - Results should be identical
    Assert.That(result1.CorrelationId, Is.EqualTo(result2.CorrelationId));
    Assert.That(result1.StartTime, Is.EqualTo(result2.StartTime));
    Assert.That(result1.EndTime, Is.EqualTo(result2.EndTime));
}
```

## 🔍 **Automated Checks**

### **Pre-commit Hook**
```bash
# Install pre-commit hook
pre-commit install

# Run deterministic check manually
.\scripts\check-orchestrator-determinism.ps1
```

### **CI/CD Pipeline**
The pipeline automatically checks all orchestrator functions for deterministic violations and fails the build if any are found.

### **IDE Integration**
Install the custom analyzer package to get real-time feedback in your IDE:

```xml
<PackageReference Include="BigCommerce.Migration.CodeAnalysis" Version="1.0.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

## 📋 **Checklist for New Orchestrators**

Before committing orchestrator functions, verify:

- [ ] No `DateTime.Now` or `DateTime.UtcNow` usage
- [ ] No `Guid.NewGuid()` usage
- [ ] No `Task.Delay()` or `Thread.Sleep()` usage
- [ ] No HTTP calls or external I/O
- [ ] No `ConfigureAwait(false)` usage
- [ ] No random number generation
- [ ] All I/O operations moved to activity functions
- [ ] Used `context.CreateReplaySafeLogger()` for logging
- [ ] Added unit tests for deterministic behavior
- [ ] Ran `.\scripts\check-orchestrator-determinism.ps1` locally

## 🆘 **Common Mistakes**

### **1. Hidden Non-Determinism**
```csharp
// ❌ Subtle non-determinism
var timeout = TimeSpan.FromMinutes(DateTime.Now.Minute); // Uses current minute!

// ✅ Deterministic timeout
var timeout = TimeSpan.FromMinutes(5); // Fixed timeout
```

### **2. Conditional Non-Determinism**
```csharp
// ❌ Non-deterministic only sometimes
if (someCondition)
{
    var id = Guid.NewGuid(); // ❌ Only fails if condition is true
}

// ✅ Always deterministic
if (someCondition)
{
    var id = context.NewGuid(); // ✅ Always uses context
}
```

### **3. Dependency Injection Issues**
```csharp
// ❌ Injecting non-deterministic services
public MyOrchestrator(IDateTimeProvider dateTimeProvider) // ❌ Could use DateTime.Now

// ✅ Use context for deterministic operations
public MyOrchestrator(ILogger<MyOrchestrator> logger) // ✅ Safe dependency
```

## 🔗 **Resources**

- [Azure Durable Functions Code Constraints](https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-code-constraints)
- [Durable Functions Best Practices](https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-best-practices)
- [BigCommerce Migration Architecture](docs/Azure-Durable-Functions-Deterministic-Architecture.md)

## 🤝 **Getting Help**

If you encounter issues with deterministic behavior:

1. Check this guide for common patterns
2. Run the automated checker: `.\scripts\check-orchestrator-determinism.ps1`
3. Review the CI/CD pipeline error messages
4. Consult the team's architecture documentation
5. Ask in team channels for specific guidance

---

**Remember**: Deterministic behavior is not optional - it's a requirement for reliable Azure Durable Functions! 🎯 