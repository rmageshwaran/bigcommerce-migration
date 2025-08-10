# Circuit Breaker Pattern Implementation - TODO Document

## 🎯 **PROJECT OVERVIEW**

**Project**: Circuit Breaker Pattern Implementation for BigCommerce Migration System  
**Goal**: Implement circuit breaker pattern to prevent cascade failures and improve system resilience  
**Current Status**: 🔴 **NOT STARTED** - Ready for implementation  
**Priority**: **MEDIUM** - Significant performance and resilience benefits  
**Estimated Effort**: 16 hours total

---

## 🚀 **STRATEGIC BENEFITS**

### **Performance Improvements**
- **99%+ reduction in wasted time** during API failures
- **Better resource utilization** during outages
- **Improved user experience** with clear error messages
- **Cascading failure prevention** across entity types

### **Resilience Enhancements**
- **Fail-fast behavior** when APIs are down
- **Automatic recovery testing** when services return
- **Rate limit quota preservation** by avoiding failed requests
- **Better system stability** under adverse conditions

---

## 📋 **IMPLEMENTATION TASKS**

## **Phase 1: Core Circuit Breaker Infrastructure** (8 hours)

### **CB-T1: Implement Core Circuit Breaker Classes** ⭐
**Priority**: Critical | **Effort**: 4 hours | **Dependencies**: None

**Sub-tasks:**
1. **CB-T1.1**: Create CircuitBreakerState and supporting models
   ```csharp
   public enum CircuitStatus { Closed, Open, HalfOpen }
   
   public class CircuitBreakerState
   {
       public CircuitStatus Status { get; set; } = CircuitStatus.Closed;
       public int FailureCount { get; set; } = 0;
       public DateTime LastFailureTime { get; set; }
       public DateTime NextAttemptTime { get; set; }
       public TimeSpan BreakDuration { get; set; } = TimeSpan.FromMinutes(2);
       public int FailureThreshold { get; set; } = 3;
   }
   
   public class CircuitBreakerOpenException : Exception
   {
       public string Endpoint { get; }
       public DateTime NextAttemptTime { get; }
       
       public CircuitBreakerOpenException(string endpoint, DateTime nextAttemptTime, string message) 
           : base(message)
       {
           Endpoint = endpoint;
           NextAttemptTime = nextAttemptTime;
       }
   }
   ```

2. **CB-T1.2**: Implement ApiCircuitBreaker core class
   ```csharp
   public class ApiCircuitBreaker : IApiCircuitBreaker
   {
       private readonly ConcurrentDictionary<string, CircuitBreakerState> _circuitStates = new();
       private readonly SemaphoreSlim _stateLock = new(1, 1);
       private readonly ILogger<ApiCircuitBreaker> _logger;
       
       public async Task<T> ExecuteWithCircuitBreakerAsync<T>(
           string endpoint, 
           Func<Task<T>> operation,
           CircuitBreakerConfig config = null,
           CancellationToken cancellationToken = default)
       {
           var circuitKey = GetCircuitKey(endpoint);
           var state = await GetCircuitStateAsync(circuitKey);
           
           switch (state.Status)
           {
               case CircuitStatus.Closed:
                   return await ExecuteOperationAsync(operation, state, circuitKey, config);
                   
               case CircuitStatus.Open:
                   if (DateTime.UtcNow < state.NextAttemptTime)
                   {
                       throw new CircuitBreakerOpenException(
                           endpoint, 
                           state.NextAttemptTime,
                           $"Circuit breaker is OPEN for {endpoint}. Next attempt allowed at {state.NextAttemptTime:HH:mm:ss}");
                   }
                   await TransitionToHalfOpenAsync(circuitKey);
                   goto case CircuitStatus.HalfOpen;
                   
               case CircuitStatus.HalfOpen:
                   return await ExecuteTestRequestAsync(operation, state, circuitKey, config);
                   
               default:
                   throw new InvalidOperationException($"Unknown circuit status: {state.Status}");
           }
       }
   }
   ```

3. **CB-T1.3**: Create CircuitBreakerConfig for different entity types
   ```csharp
   public class CircuitBreakerConfig
   {
       public int FailureThreshold { get; set; } = 3;
       public TimeSpan BreakDuration { get; set; } = TimeSpan.FromMinutes(2);
       public int HalfOpenMaxRequests { get; set; } = 1;
       public List<Type> ExceptionsToTrip { get; set; } = new();
       public List<Type> ExceptionsToIgnore { get; set; } = new();
   }
   
   public static class CircuitBreakerConfigs
   {
       public static CircuitBreakerConfig Products => new()
       {
           FailureThreshold = 3,
           BreakDuration = TimeSpan.FromMinutes(2),
           ExceptionsToTrip = { typeof(HttpRequestException) },
           ExceptionsToIgnore = { typeof(OperationCanceledException) }
       };
       
       public static CircuitBreakerConfig Options => new()
       {
           FailureThreshold = 5, // More tolerant for options
           BreakDuration = TimeSpan.FromMinutes(1),
           ExceptionsToTrip = { typeof(HttpRequestException) },
           ExceptionsToIgnore = { typeof(OperationCanceledException) }
       };
       
       public static CircuitBreakerConfig Images => new()
       {
           FailureThreshold = 8, // Most tolerant for images
           BreakDuration = TimeSpan.FromMinutes(1),
           ExceptionsToTrip = { typeof(HttpRequestException) },
           ExceptionsToIgnore = { typeof(OperationCanceledException) }
       };
   }
   ```

**Acceptance Criteria:**
- ✅ Core circuit breaker classes implemented
- ✅ Configurable thresholds and break durations
- ✅ Thread-safe state management
- ✅ Proper exception handling and logging

**Files to Create:**
- `src/BigCommerce.Migration.Core/Services/ApiCircuitBreaker.cs`
- `src/BigCommerce.Migration.Core/Models/CircuitBreakerModels.cs`
- `src/BigCommerce.Migration.Core/Interfaces/IApiCircuitBreaker.cs`

---

### **CB-T2: Implement Enhanced API Request Handler** ⭐
**Priority**: Critical | **Effort**: 3 hours | **Dependencies**: CB-T1

**Sub-tasks:**
1. **CB-T2.1**: Create EnhancedApiRequestHandler decorator
   ```csharp
   public class EnhancedApiRequestHandler : IApiRequestHandler
   {
       private readonly IApiRequestHandler _baseHandler;
       private readonly IApiCircuitBreaker _circuitBreaker;
       private readonly IDynamicRateLimiter _rateLimiter;
       private readonly ILogger<EnhancedApiRequestHandler> _logger;
       
       public async Task<T> ExecuteRequestAsync<T>(ApiRequest request, CancellationToken cancellationToken = default)
       {
           var endpoint = GetEndpointKey(request.Url);
           var config = GetCircuitBreakerConfig(endpoint);
           
           return await _circuitBreaker.ExecuteWithCircuitBreakerAsync(
               endpoint,
               async () =>
               {
                   // 1. Circuit breaker allows request
                   // 2. Apply existing rate limiting (unchanged)
                   await _rateLimiter.CheckAndWaitAsync(request.StoreConfiguration.StoreId!, cancellationToken);
                   
                   // 3. Execute request with existing handler (unchanged)
                   return await _baseHandler.ExecuteRequestAsync<T>(request, cancellationToken);
               },
               config,
               cancellationToken);
       }
       
       private CircuitBreakerConfig GetCircuitBreakerConfig(string endpoint)
       {
           return endpoint switch
           {
               var e when e.Contains("/products") => CircuitBreakerConfigs.Products,
               var e when e.Contains("/options") => CircuitBreakerConfigs.Options,
               var e when e.Contains("/images") => CircuitBreakerConfigs.Images,
               _ => CircuitBreakerConfigs.Products // Default
           };
       }
   }
   ```

2. **CB-T2.2**: Update dependency injection configuration
   ```csharp
   // In ServiceCollectionExtensions.cs
   public static IServiceCollection AddCircuitBreakerServices(this IServiceCollection services, IConfiguration configuration)
   {
       // Register circuit breaker services
       services.AddSingleton<IApiCircuitBreaker, ApiCircuitBreaker>();
       
       // Conditionally wrap ApiRequestHandler with circuit breaker
       services.AddSingleton<IApiRequestHandler>(provider =>
       {
           var baseHandler = new ApiRequestHandler(/* existing dependencies */);
           
           if (configuration.GetValue<bool>("CircuitBreaker:Enabled", false))
           {
               var circuitBreaker = provider.GetRequiredService<IApiCircuitBreaker>();
               return new EnhancedApiRequestHandler(baseHandler, circuitBreaker, /* other deps */);
           }
           
           return baseHandler; // Existing behavior unchanged
       });
       
       return services;
   }
   ```

3. **CB-T2.3**: Add configuration settings
   ```json
   // In appsettings.json
   {
     "CircuitBreaker": {
       "Enabled": true,
       "DefaultFailureThreshold": 3,
       "DefaultBreakDuration": "00:02:00",
       "EndpointConfigurations": {
         "products": {
           "failureThreshold": 3,
           "breakDuration": "00:02:00"
         },
         "options": {
           "failureThreshold": 5,
           "breakDuration": "00:01:00"
         },
         "images": {
           "failureThreshold": 8,
           "breakDuration": "00:01:00"
         }
       }
     }
   }
   ```

**Acceptance Criteria:**
- ✅ Non-breaking integration with existing ApiRequestHandler
- ✅ Configuration-driven enable/disable
- ✅ Preserves existing rate limiting functionality
- ✅ Entity-specific circuit breaker configurations

**Files to Modify:**
- `src/BigCommerce.Migration.Infrastructure/Services/EnhancedApiRequestHandler.cs` (new)
- `src/BigCommerce.Migration.Infrastructure/Extensions/ServiceCollectionExtensions.cs`
- `src/BigCommerce.Migration.Functions/appsettings.json`

---

### **CB-T3: Integration with Dynamic Rate Limiting** ⭐
**Priority**: High | **Effort**: 1 hour | **Dependencies**: CB-T2

**Sub-tasks:**
1. **CB-T3.1**: Enhance ApiHealthMonitor with circuit breaker health
   ```csharp
   public class EnhancedApiHealthMonitor : IApiHealthMonitor
   {
       private readonly IApiHealthMonitor _baseMonitor;
       private readonly IApiCircuitBreaker _circuitBreaker;
       
       public async Task<ApiHealthMetrics> GetApiHealthAsync(string storeId, CancellationToken cancellationToken = default)
       {
           var baseHealth = await _baseMonitor.GetApiHealthAsync(storeId, cancellationToken);
           var circuitHealth = _circuitBreaker.GetCircuitHealth(storeId);
           
           return new ApiHealthMetrics
           {
               HealthScore = CalculateEnhancedHealthScore(baseHealth, circuitHealth),
               ResponseTime = baseHealth.ResponseTime,
               RequestsLeft = baseHealth.RequestsLeft,
               RequestsQuota = baseHealth.RequestsQuota,
               CircuitStatus = circuitHealth.Status,
               FailureRate = circuitHealth.FailureRate,
               LastFailureTime = circuitHealth.LastFailureTime
           };
       }
       
       private double CalculateEnhancedHealthScore(ApiHealthMetrics baseHealth, CircuitBreakerHealth circuitHealth)
       {
           var baseScore = baseHealth.GetHealthScore();
           
           // Reduce health score if circuit is open or recently failed
           return circuitHealth.Status switch
           {
               CircuitStatus.Open => Math.Min(baseScore * 0.1, 10), // Very low health when circuit is open
               CircuitStatus.HalfOpen => Math.Min(baseScore * 0.5, 50), // Reduced health when testing
               CircuitStatus.Closed when circuitHealth.FailureRate > 0.1 => baseScore * 0.8, // Slightly reduced if recent failures
               _ => baseScore // Normal health when circuit is closed and stable
           };
       }
   }
   ```

**Acceptance Criteria:**
- ✅ Circuit breaker state incorporated into health metrics
- ✅ Enhanced health scoring with circuit breaker data
- ✅ No conflicts with existing rate limiting logic

---

## **Phase 2: Enhanced Features and Monitoring** (8 hours)

### **CB-T4: Circuit Breaker Monitoring and Metrics** 
**Priority**: Medium | **Effort**: 3 hours | **Dependencies**: CB-T3

**Sub-tasks:**
1. **CB-T4.1**: Add circuit breaker telemetry
   ```csharp
   public class CircuitBreakerTelemetry
   {
       private readonly IMetrics _metrics;
       
       public void RecordCircuitStateChange(string endpoint, CircuitStatus fromStatus, CircuitStatus toStatus)
       {
           _metrics.Measure.Counter.Increment("circuit_breaker.state_changes", 
               new MetricTags("endpoint", endpoint, "from_status", fromStatus.ToString(), "to_status", toStatus.ToString()));
       }
       
       public void RecordCircuitBreakerBlock(string endpoint, TimeSpan blockDuration)
       {
           _metrics.Measure.Counter.Increment("circuit_breaker.blocks",
               new MetricTags("endpoint", endpoint));
           _metrics.Measure.Histogram.Update("circuit_breaker.block_duration",
               blockDuration.TotalMilliseconds,
               new MetricTags("endpoint", endpoint));
       }
       
       public void RecordCircuitBreakerFailure(string endpoint, string exceptionType)
       {
           _metrics.Measure.Counter.Increment("circuit_breaker.failures",
               new MetricTags("endpoint", endpoint, "exception_type", exceptionType));
       }
   }
   ```

2. **CB-T4.2**: Create circuit breaker dashboard endpoint
   ```csharp
   [Function("GetCircuitBreakerStatus")]
   public async Task<HttpResponseData> GetCircuitBreakerStatus(
       [HttpTrigger(AuthorizationLevel.Function, "get", Route = "circuit-breaker/status")] HttpRequestData req)
   {
       var circuitStatus = await _circuitBreaker.GetAllCircuitStatusAsync();
       
       var response = req.CreateResponse(HttpStatusCode.OK);
       await response.WriteAsJsonAsync(new
       {
           circuits = circuitStatus.Select(c => new
           {
               endpoint = c.Endpoint,
               status = c.Status.ToString(),
               failureCount = c.FailureCount,
               lastFailureTime = c.LastFailureTime,
               nextAttemptTime = c.NextAttemptTime,
               isHealthy = c.Status == CircuitStatus.Closed
           })
       });
       
       return response;
   }
   ```

**Acceptance Criteria:**
- ✅ Comprehensive circuit breaker metrics
- ✅ Dashboard endpoint for monitoring
- ✅ Integration with existing telemetry system

---

### **CB-T5: Advanced Circuit Breaker Features**
**Priority**: Low | **Effort**: 4 hours | **Dependencies**: CB-T4

**Sub-tasks:**
1. **CB-T5.1**: Implement adaptive threshold adjustment
   ```csharp
   public class AdaptiveCircuitBreaker : ApiCircuitBreaker
   {
       public void AdjustThresholdBasedOnHistory(string endpoint, TimeSpan lookbackPeriod)
       {
           var recentFailures = GetRecentFailures(endpoint, lookbackPeriod);
           var averageFailureRate = CalculateAverageFailureRate(recentFailures);
           
           // Adjust threshold based on historical performance
           if (averageFailureRate < 0.01) // Very stable endpoint
           {
               IncreaseThreshold(endpoint); // Less sensitive
           }
           else if (averageFailureRate > 0.1) // Unstable endpoint
           {
               DecreaseThreshold(endpoint); // More sensitive
           }
       }
   }
   ```

2. **CB-T5.2**: Add circuit breaker bulk operations
   ```csharp
   public async Task ResetAllCircuitsAsync()
   {
       await _stateLock.WaitAsync();
       try
       {
           foreach (var circuit in _circuitStates.Values)
           {
               circuit.Status = CircuitStatus.Closed;
               circuit.FailureCount = 0;
           }
           
           _logger.LogInformation("All circuit breakers have been reset to CLOSED state");
       }
       finally
       {
           _stateLock.Release();
       }
   }
   
   public async Task<Dictionary<string, CircuitBreakerHealth>> GetCircuitBreakerHealthSummaryAsync()
   {
       return _circuitStates.ToDictionary(
           kvp => kvp.Key,
           kvp => new CircuitBreakerHealth
           {
               Status = kvp.Value.Status,
               FailureCount = kvp.Value.FailureCount,
               FailureRate = CalculateFailureRate(kvp.Key),
               LastFailureTime = kvp.Value.LastFailureTime
           });
   }
   ```

3. **CB-T5.3**: Add manual circuit breaker control
   ```csharp
   [Function("ManualCircuitBreakerControl")]
   public async Task<HttpResponseData> ManualCircuitBreakerControl(
       [HttpTrigger(AuthorizationLevel.Function, "post", Route = "circuit-breaker/{endpoint}/control")] HttpRequestData req,
       string endpoint)
   {
       var body = await req.ReadAsStringAsync();
       var command = JsonSerializer.Deserialize<CircuitBreakerCommand>(body);
       
       switch (command.Action.ToLower())
       {
           case "open":
               await _circuitBreaker.ForceOpenAsync(endpoint, command.Duration);
               break;
           case "close":
               await _circuitBreaker.ForceCloseAsync(endpoint);
               break;
           case "reset":
               await _circuitBreaker.ResetAsync(endpoint);
               break;
       }
       
       var response = req.CreateResponse(HttpStatusCode.OK);
       await response.WriteAsJsonAsync(new { success = true, endpoint, action = command.Action });
       return response;
   }
   ```

**Acceptance Criteria:**
- ✅ Adaptive threshold adjustment based on historical data
- ✅ Bulk circuit breaker operations
- ✅ Manual control capabilities for operational scenarios

---

### **CB-T6: Testing and Validation**
**Priority**: Medium | **Effort**: 1 hour | **Dependencies**: CB-T5

**Sub-tasks:**
1. **CB-T6.1**: Create circuit breaker unit tests
   ```csharp
   [Test]
   public async Task CircuitBreaker_AfterThresholdFailures_ShouldOpenCircuit()
   {
       // Arrange
       var circuitBreaker = new ApiCircuitBreaker(logger, config);
       var endpoint = "test-endpoint";
       
       // Act - cause failures to reach threshold
       for (int i = 0; i < 3; i++)
       {
           try
           {
               await circuitBreaker.ExecuteWithCircuitBreakerAsync(endpoint, 
                   () => throw new HttpRequestException("Test failure"));
           }
           catch (HttpRequestException) { /* Expected */ }
       }
       
       // Assert - circuit should be open
       var exception = await Assert.ThrowsAsync<CircuitBreakerOpenException>(
           () => circuitBreaker.ExecuteWithCircuitBreakerAsync(endpoint, 
               () => Task.FromResult("success")));
       
       exception.Endpoint.Should().Be(endpoint);
   }
   
   [Test]
   public async Task CircuitBreaker_WhenOpen_ShouldFailFast()
   {
       // Test that circuit breaker fails fast without calling underlying operation
   }
   
   [Test]
   public async Task CircuitBreaker_InHalfOpenState_ShouldAllowTestRequest()
   {
       // Test half-open behavior and recovery
   }
   ```

2. **CB-T6.2**: Create integration tests
   ```csharp
   [Test]
   public async Task EnhancedApiRequestHandler_WithCircuitBreaker_ShouldIntegrateCorrectly()
   {
       // Test full integration with existing ApiRequestHandler
   }
   
   [Test]
   public async Task CircuitBreaker_WithRateLimiting_ShouldNotConflict()
   {
       // Test that circuit breaker works correctly with rate limiting
   }
   ```

**Acceptance Criteria:**
- ✅ Comprehensive unit test coverage
- ✅ Integration tests with existing system
- ✅ Performance tests demonstrating benefits

---

## 🧪 **TESTING STRATEGY**

### **Unit Testing Requirements**
- Circuit breaker state transitions (Closed → Open → HalfOpen → Closed)
- Threshold-based failure detection
- Timeout and recovery behavior
- Thread safety under concurrent load
- Configuration-driven behavior

### **Integration Testing Requirements**
- Integration with existing ApiRequestHandler
- Compatibility with DynamicRateLimiter
- SignalR event integration
- Dashboard endpoint functionality

### **Performance Testing Requirements**
- Demonstrate fail-fast behavior vs. timeout behavior
- Validate no performance regression during normal operation
- Measure improvement during API failure scenarios

---

## 📊 **SUCCESS METRICS**

### **Performance Metrics**
- **Time to Failure Detection**: < 30 seconds from first failure to circuit open
- **Fail-Fast Response Time**: < 10ms for blocked requests
- **Recovery Time**: < 60 seconds from API recovery to circuit close
- **Resource Savings**: 99%+ reduction in wasted processing during API failures

### **Reliability Metrics**
- **False Positive Rate**: < 1% (circuits opening due to transient issues)
- **Recovery Success Rate**: > 95% (successful recovery when APIs return)
- **Integration Compatibility**: 100% compatibility with existing functionality

---

## 🚨 **RISKS AND MITIGATION**

### **Implementation Risks**
1. **Breaking Changes**: Use decorator pattern to avoid breaking existing API
2. **Performance Overhead**: Minimize overhead through efficient state management
3. **Configuration Complexity**: Provide sensible defaults and clear documentation

### **Operational Risks**
1. **False Circuit Opens**: Implement proper threshold tuning and monitoring
2. **Manual Intervention**: Provide manual control endpoints for emergency scenarios
3. **Monitoring Gaps**: Comprehensive telemetry and dashboard integration

---

## 🎯 **IMPLEMENTATION PRIORITY**

### **High Priority (Implement First)**
- CB-T1: Core Circuit Breaker Infrastructure
- CB-T2: Enhanced API Request Handler
- CB-T3: Dynamic Rate Limiting Integration

### **Medium Priority (Implement Next)**
- CB-T4: Monitoring and Metrics
- CB-T6: Testing and Validation

### **Low Priority (Nice to Have)**
- CB-T5: Advanced Features (Adaptive thresholds, manual control)

---

## 📋 **CONFIGURATION REFERENCE**

### **Environment Variables**
```bash
# Docker compose additions
- CircuitBreaker__Enabled=true
- CircuitBreaker__DefaultFailureThreshold=3
- CircuitBreaker__DefaultBreakDuration=00:02:00
- CircuitBreaker__Products__FailureThreshold=3
- CircuitBreaker__Options__FailureThreshold=5
- CircuitBreaker__Images__FailureThreshold=8
```

### **AppSettings Configuration**
```json
{
  "CircuitBreaker": {
    "Enabled": true,
    "DefaultFailureThreshold": 3,
    "DefaultBreakDuration": "00:02:00",
    "HalfOpenMaxRequests": 1,
    "EndpointConfigurations": {
      "products": { "failureThreshold": 3, "breakDuration": "00:02:00" },
      "options": { "failureThreshold": 5, "breakDuration": "00:01:00" },
      "images": { "failureThreshold": 8, "breakDuration": "00:01:00" },
      "modifiers": { "failureThreshold": 5, "breakDuration": "00:01:00" },
      "reviews": { "failureThreshold": 5, "breakDuration": "00:01:00" }
    }
  }
}
```

---

**🎯 Ready for Circuit Breaker implementation! This solution provides comprehensive resilience improvements with zero breaking changes to existing functionality.** 🚀