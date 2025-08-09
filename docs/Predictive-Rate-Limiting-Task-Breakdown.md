# Predictive Rate Limiting - Detailed Task Breakdown

## 🎯 **System Guarantees**
1. **✅ Zero 429 Errors**: 99.9% guarantee through distributed consensus and circuit breakers
2. **✅ Universal Coordination**: All app instances participate in coordinated rate limiting
3. **✅ Smart Detection**: Real-time BigCommerce quota tracking and adaptive responses  
4. **✅ Transparent Operation**: Minimal application code changes required

## 📋 **Revised Task Categories**

### 🏗️ Foundation Tasks (Integration-First)
### 🧠 Consensus Tasks (Distributed Algorithms) 
### 🔗 Integration Tasks (Existing System Enhancement)
### 🧪 Multi-Instance Testing (Coordination Validation)
### 🚀 Health-Monitored Deployment (Circuit Breaker Rollout)

---

## 🏗️ Infrastructure Tasks

### I-001: Enhanced Table Storage Integration
**Effort**: 3 hours | **Priority**: High | **Dependencies**: None

**Tasks**:
- [ ] **Leverage existing infrastructure**: Extend `MigrationStorageService` patterns
- [ ] **Reuse connection management**: Follow `AzureTableDistributedLockService` approach
- [ ] **Integrate with existing config**: Use `AzureWebJobsStorage` connection string
- [ ] **Create predictive tables**: `RateLimitQuotas`, `RateLimitInstances`, `RateLimitTokens`
- [ ] **Auto-table creation**: Follow existing `MigrationStorageService` auto-setup patterns

**Acceptance Criteria**:
- ✅ **Zero new infrastructure**: Reuses existing Table Storage setup
- ✅ **Consistent patterns**: Follows established connection and retry patterns
- ✅ **Automatic setup**: Tables created on first predictive rate limiting activation

**Files to Create/Modify**:
- `src/BigCommerce.Migration.Core/BigCommerce.Migration.Core.csproj`
- `src/BigCommerce.Migration.Core/Interfaces/ITableStorageClientFactory.cs`
- `src/BigCommerce.Migration.Infrastructure/Services/TableStorageClientFactory.cs`
- `src/BigCommerce.Migration.Functions/appsettings.json`

---

### I-002: Entity Models Creation
**Effort**: 3 hours | **Priority**: High | **Dependencies**: I-001

**Tasks**:
- [ ] Create `StoreQuotaEntity` with ITableEntity implementation
- [ ] Create `InstanceEntity` for multi-instance coordination
- [ ] Create `TokenReservationEntity` for atomic operations
- [ ] Add validation attributes and XML documentation
- [ ] Implement ToString() methods for debugging

**Acceptance Criteria**:
- All entities implement ITableEntity correctly
- ETag properties support optimistic concurrency
- Proper partition key/row key design for performance

**Files to Create**:
- `src/BigCommerce.Migration.Core/Models/RateLimiting/StoreQuotaEntity.cs`
- `src/BigCommerce.Migration.Core/Models/RateLimiting/InstanceEntity.cs`
- `src/BigCommerce.Migration.Core/Models/RateLimiting/TokenReservationEntity.cs`

---

### I-003: Configuration Models
**Effort**: 2 hours | **Priority**: Medium | **Dependencies**: None

**Tasks**:
- [ ] Create `PredictiveRateLimitingConfiguration` class
- [ ] Add feature flags for gradual rollout
- [ ] Add safety buffer configuration
- [ ] Add token management settings
- [ ] Add Table Storage connection settings

**Acceptance Criteria**:
- Configuration validates on startup
- Default values are production-safe
- Feature flags allow instant enable/disable

**Files to Create**:
- `src/BigCommerce.Migration.Core/Models/RateLimiting/PredictiveRateLimitingConfiguration.cs`

---

## 🧠 Logic Tasks

### L-001: API Header Parsing
**Effort**: 2 hours | **Priority**: High | **Dependencies**: None

**Tasks**:
- [ ] Create `ApiRateLimitInfo` model for header data
- [ ] Implement parsing for X-Rate-Limit-* headers
- [ ] Add health scoring based on quota utilization
- [ ] Add safety buffer calculation methods
- [ ] Handle missing or malformed headers gracefully

**Acceptance Criteria**:
- Parses BigCommerce headers accurately
- Works with other APIs using same header format
- Calculates health scores and safety buffers correctly
- Graceful handling of edge cases

**Files to Create**:
- `src/BigCommerce.Migration.Core/Models/RateLimiting/ApiRateLimitInfo.cs`

**Test Cases**:
```csharp
// Valid headers
X-Rate-Limit-Requests-Quota: 500
X-Rate-Limit-Requests-Left: 387
X-Rate-Limit-Time-Window-Ms: 60000
X-Rate-Limit-Time-Reset-Ms: 45000

// Missing headers (should return null)
// Malformed headers (should return null)
// Different quota values (should handle dynamically)
```

---

### L-002: Quota Tracking Logic
**Effort**: 4 hours | **Priority**: High | **Dependencies**: I-001, I-002, L-001

**Tasks**:
- [ ] Implement `IQuotaTracker` interface
- [ ] Create `DistributedQuotaTracker` with atomic updates
- [ ] Add ETag-based conflict resolution with retry logic
- [ ] Implement quota window reset detection
- [ ] Add throttling to prevent excessive Table Storage operations

**Acceptance Criteria**:
- Updates quota atomically using ETag CAS
- Handles quota window resets correctly
- Throttles updates (max 1 per 5 seconds per store)
- Retries ETag conflicts with exponential backoff + jitter

**Files to Create**:
- `src/BigCommerce.Migration.Core/Interfaces/IQuotaTracker.cs`
- `src/BigCommerce.Migration.Infrastructure/Services/DistributedQuotaTracker.cs`

**Algorithm**:
```csharp
for (int retry = 0; retry < maxRetries; retry++)
{
    var entity = await GetQuotaEntity(storeId);
    entity.UpdateFromHeaders(headers);
    
    try
    {
        await tableClient.UpdateEntityAsync(entity, entity.ETag);
        return; // Success
    }
    catch (RequestFailedException ex) when (ex.Status == 412)
    {
        await Task.Delay(CalculateJitteredDelay(retry));
    }
}
```

---

### L-003: Instance Coordination
**Effort**: 3 hours | **Priority**: High | **Dependencies**: I-001, I-002

**Tasks**:
- [ ] Implement `InstanceManager` for multi-instance discovery
- [ ] Add heartbeat mechanism (60-second intervals)
- [ ] Implement active instance counting
- [ ] Add cleanup of stale instances (>2 minutes old)
- [ ] Track instance performance metrics

**Acceptance Criteria**:
- Instances register automatically on startup
- Heartbeats maintain active status
- Stale instances cleaned up automatically
- Accurate count of active instances per store

**Files to Create**:
- `src/BigCommerce.Migration.Infrastructure/Services/InstanceManager.cs`

**Heartbeat Logic**:
```csharp
public async Task SendHeartbeatAsync(string storeId)
{
    var entity = new InstanceEntity
    {
        PartitionKey = storeId,
        RowKey = _instanceId,
        LastHeartbeat = DateTimeOffset.UtcNow,
        // ... other properties
    };
    
    await _instanceTable.UpsertEntityAsync(entity);
}
```

---

### L-004: Token Management
**Effort**: 4 hours | **Priority**: High | **Dependencies**: L-002, L-003

**Tasks**:
- [ ] Implement `PredictiveTokenManager` for fair distribution
- [ ] Add 15% safety buffer calculations
- [ ] Implement atomic token reservation using ETag CAS
- [ ] Add 30-second token expiry mechanism
- [ ] Handle token consumption and tracking

**Acceptance Criteria**:
- Distributes tokens fairly across instances
- Applies appropriate safety buffers (15%/25%/50%)
- Reserves tokens atomically to prevent race conditions
- Expires unused tokens to prevent hoarding

**Files to Create**:
- `src/BigCommerce.Migration.Infrastructure/Services/PredictiveTokenManager.cs`

**Token Distribution Algorithm**:
```csharp
public async Task<int> ReserveTokensAsync(string storeId)
{
    var quota = await _quotaTracker.GetCurrentQuotaAsync(storeId);
    var activeInstances = await _instanceManager.GetActiveCountAsync(storeId);
    
    var safeTokens = quota.GetSafeTokens(GetSafetyBuffer(quota));
    var tokensPerInstance = Math.Max(1, safeTokens / activeInstances);
    
    return await ReserveWithCAS(storeId, tokensPerInstance);
}
```

---

## 🔗 Integration Tasks

### IN-001: Service Adapter Creation
**Effort**: 2 hours | **Priority**: High | **Dependencies**: L-002, L-004

**Tasks**:
- [ ] Create `DistributedRateLimitService` implementing `IRateLimitService`
- [ ] Ensure zero breaking changes for existing callers
- [ ] Maintain same method signatures and behavior
- [ ] Add feature flag to switch between implementations

**Acceptance Criteria**:
- Existing code works without modifications
- Feature flag instantly switches between memory/distributed
- All `IRateLimitService` methods implemented correctly

**Files to Create**:
- `src/BigCommerce.Migration.Infrastructure/Services/DistributedRateLimitService.cs`

---

### IN-002: API Handler Integration
**Effort**: 1 hour | **Priority**: High | **Dependencies**: L-001, IN-001

**Tasks**:
- [ ] Modify `ApiRequestHandler` to parse headers on every response
- [ ] Integrate quota tracker updates automatically
- [ ] Ensure no performance impact on API calls
- [ ] Add logging for header parsing results

**Acceptance Criteria**:
- Headers parsed on every BigCommerce API response
- Quota tracker updated automatically
- No additional latency for API calls
- Graceful handling of parsing failures

**Files to Modify**:
- `src/BigCommerce.Migration.Infrastructure/Services/ApiRequestHandler.cs`

**Integration Point**:
```csharp
// In ProcessResponseAsync method
if (_quotaTracker != null && response.IsSuccessStatusCode)
{
    _ = Task.Run(async () =>
    {
        await _quotaTracker.UpdateQuotaFromHeadersAsync(
            storeId, response, request.Url, request.Method);
    });
}
```

---

### IN-003: Dependency Injection Setup
**Effort**: 2 hours | **Priority**: Medium | **Dependencies**: All Logic Tasks

**Tasks**:
- [ ] Register services in `ServiceCollectionExtensions`
- [ ] Add conditional registration based on feature flag
- [ ] Ensure proper service lifetimes (Singleton/Scoped)
- [ ] Add configuration binding

**Acceptance Criteria**:
- Services registered with correct lifetimes
- Feature flag controls which implementation is used
- Configuration bound properly
- No circular dependencies

**Files to Modify**:
- `src/BigCommerce.Migration.Functions/Extensions/ServiceCollectionExtensions.cs`
- `src/BigCommerce.Migration.Infrastructure/Extensions/ServiceCollectionExtensions.cs`

---

## 🧪 Testing Tasks

### T-001: Unit Tests - Core Logic
**Effort**: 6 hours | **Priority**: High | **Dependencies**: All Logic Tasks

**Tasks**:
- [ ] Test header parsing accuracy
- [ ] Test safety buffer calculations
- [ ] Test token distribution fairness
- [ ] Test ETag conflict resolution
- [ ] Test quota window reset handling

**Test Categories**:
```csharp
// Header Parsing Tests
[Test] ValidHeaders_ParsedCorrectly()
[Test] MissingHeaders_ReturnsNull()
[Test] MalformedHeaders_ReturnsNull()

// Safety Buffer Tests
[Test] HealthyQuota_Uses15PercentBuffer()
[Test] CriticalQuota_Uses50PercentBuffer()

// Token Distribution Tests
[Test] MultipleInstances_DistributedFairly()
[Test] SingleInstance_GetsAllTokens()

// ETag Conflict Tests
[Test] ConflictRetry_WithExponentialBackoff()
[Test] MaxRetriesExceeded_LogsWarning()
```

**Files to Create**:
- `tests/BigCommerce.Migration.UnitTests/RateLimiting/ApiRateLimitInfoTests.cs`
- `tests/BigCommerce.Migration.UnitTests/RateLimiting/DistributedQuotaTrackerTests.cs`
- `tests/BigCommerce.Migration.UnitTests/RateLimiting/PredictiveTokenManagerTests.cs`

---

### T-002: Integration Tests - Table Storage
**Effort**: 4 hours | **Priority**: High | **Dependencies**: T-001

**Tasks**:
- [ ] Test real Table Storage operations
- [ ] Test multi-instance coordination
- [ ] Test ETag conflicts under concurrency
- [ ] Test table auto-creation
- [ ] Test connection failure scenarios

**Test Scenarios**:
```csharp
[Test] MultipleInstances_CoordinateCorrectly()
[Test] ConcurrentUpdates_HandleETagConflicts()
[Test] ConnectionFailure_FallsBackGracefully()
[Test] TablesAutoCreated_OnFirstRun()
```

**Files to Create**:
- `tests/BigCommerce.Migration.IntegrationTests/RateLimiting/TableStorageIntegrationTests.cs`

---

### T-002.1: Multi-Instance Coordination Demonstrations
**Effort**: 2 hours | **Priority**: High | **Dependencies**: T-002

**Tasks**:
- [ ] Create comprehensive coordination demonstrations
- [ ] Test normal operations (4 instances)
- [ ] Test auto-scaling scenarios (2→4 instances)
- [ ] Test instance failure recovery
- [ ] Test critical quota protection
- [ ] Document coordination examples

**Coordination Scenarios**:

**Normal Operations (4 Instances):**
```
Test Setup: 1000 quota, 800 remaining, 25% safety buffer
Expected Result:
🎯 WebApp-1: 150 tokens (25% share)
🎯 WebApp-2: 150 tokens (25% share)  
🎯 Worker-1: 150 tokens (25% share)
🎯 Worker-2: 150 tokens (25% share)
Total: 600 tokens (100% safe quota utilization)
```

**Auto-Scaling Scenario:**
```
Before (2 instances): 300 tokens each
After (4 instances): 150 tokens each
Verification: Fair redistribution, zero interruption
```

**Instance Failure Recovery:**
```
Before (3 instances): 150 tokens each
After Failure (2 instances): 225 tokens each (+50%)
Verification: Automatic failover, increased survivor capacity
```

**Critical Quota Protection:**
```
Setup: 50 tokens remaining (critical threshold)
Expected: 8 tokens per instance (50% safety buffer)
Total: 24 allocated, 26 reserved
Verification: Enhanced protection, zero 429 errors
```

**Files to Create**:
- `tests/BigCommerce.Migration.Tests/RateLimiting/MultiInstanceDemonstrationTests.cs`

---

### T-003: Load Tests - Performance Validation
**Effort**: 3 hours | **Priority**: Medium | **Dependencies**: T-002

**Tasks**:
- [ ] Test under high concurrency (100+ threads)
- [ ] Validate 0% 429 error rate
- [ ] Measure token reservation latency
- [ ] Test ETag conflict rates under load
- [ ] Validate quota utilization efficiency

**Performance Targets**:
- Token reservation: <50ms p95
- ETag conflict rate: <5%
- 429 error rate: 0%
- Quota utilization: >90%

**Files to Create**:
- `tests/BigCommerce.Migration.PerformanceTests/RateLimiting/ConcurrencyLoadTests.cs`

---

## 🚀 Deployment Tasks

### D-001: Local Development Setup
**Effort**: 1 hour | **Priority**: Medium | **Dependencies**: All Integration Tasks

**Tasks**:
- [ ] Update local `appsettings.Development.json`
- [ ] Use Azure Storage Emulator for local testing
- [ ] Create developer setup documentation
- [ ] Add troubleshooting guide

**Acceptance Criteria**:
- Developers can run locally with feature enabled
- Clear documentation for setup
- Common issues documented with solutions

---

### D-002: Staging Deployment
**Effort**: 2 hours | **Priority**: High | **Dependencies**: All Testing Tasks

**Tasks**:
- [ ] Deploy to staging environment
- [ ] Enable for test stores only
- [ ] Monitor for 30-60 minutes
- [ ] Validate all metrics are working
- [ ] Test feature flag toggle

**Monitoring Checklist**:
- [ ] 429 error rate: 0%
- [ ] Token distribution working
- [ ] Instance coordination active
- [ ] Table Storage costs reasonable
- [ ] No performance regression

---

### D-003: Production Rollout
**Effort**: 4 hours | **Priority**: High | **Dependencies**: D-002

**Tasks**:
- [ ] Week 1: Enable for 10% of stores
- [ ] Week 2: Enable for 25% of stores
- [ ] Week 3: Enable for 50% of stores
- [ ] Week 4: Enable for 100% of stores
- [ ] Document rollback procedures

**Rollout Gates**:
- 429 error rate remains 0%
- No performance degradation
- Table Storage costs within budget
- ETag conflict rates acceptable

---

## 📊 Summary by Phase

### Phase 1: Foundation (Day 1)
**Total Effort**: 7 hours
- I-001: Table Storage Setup (2h)
- I-002: Entity Models (3h)
- I-003: Configuration (2h)

### Phase 2: Core Logic (Day 1-2)
**Total Effort**: 13 hours
- L-001: Header Parsing (2h)
- L-002: Quota Tracking (4h)
- L-003: Instance Coordination (3h)
- L-004: Token Management (4h)

### Phase 3: Integration (Day 2-3)
**Total Effort**: 5 hours
- IN-001: Service Adapter (2h)
- IN-002: API Handler Integration (1h)
- IN-003: DI Setup (2h)

### Phase 4: Testing (Day 3-4)
**Total Effort**: 13 hours
- T-001: Unit Tests (6h)
- T-002: Integration Tests (4h)
- T-003: Load Tests (3h)

### Phase 5: Deployment (Day 4-5)
**Total Effort**: 7 hours
- D-001: Local Setup (1h)
- D-002: Staging (2h)
- D-003: Production Rollout (4h)

## 🎯 Total Effort: 45 hours (5-6 working days)

### Critical Path:
I-001 → I-002 → L-002 → L-004 → IN-001 → T-001 → T-002 → D-002 → D-003

### Parallel Work Opportunities:
- Configuration (I-003) can be done in parallel with entities
- Header parsing (L-001) can be done independently
- Unit tests (T-001) can start as soon as logic is complete
- Documentation and local setup can be done in parallel

This breakdown provides a clear roadmap for implementing the predictive distributed rate limiting system with proper testing and gradual rollout.
