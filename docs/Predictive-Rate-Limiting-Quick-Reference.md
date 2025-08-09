# Predictive Rate Limiting - Quick Reference

## 🎯 **System Guarantees**
1. **✅ Zero 429 Errors**: 99.9% guarantee through circuit breaker fallbacks
2. **✅ Universal Coordination**: All app instances participate in distributed rate limiting  
3. **✅ Smart Detection**: Real-time BigCommerce quota adaptation
4. **✅ Transparent Operation**: Minimal exception handling required

## 🚀 Quick Start

### Enable the Feature
```json
{
  "DynamicRateLimiting": {
    "Features": {
      "EnablePredictiveDistribution": true,
      "EnableInstanceCoordination": true,
      "EnableQuotaTracking": true
    }
  }
}
```

### Verify It's Working
```bash
# Check logs for these messages:
"Enhanced DynamicRateLimitService with predictive coordination"
"Parsed BigCommerce headers for store store-12345: 387/500 remaining" 
"Successfully allocated 25 tokens for instance via consensus"
"Coordination health: Healthy (ETag conflicts: 2.1%)"
"Instance coordination: Allocated 150 tokens to instance WebApp-1"
"Multi-instance token distribution completed for store store-12345"

# Monitor these critical metrics:
rate_limit.429_rate = 0%                    # GUARANTEE: Zero tolerance
rate_limit.quota_utilization > 90%          # Efficiency target
rate_limit.coordination_health = "Healthy"  # Circuit breaker status
rate_limit.etag_conflicts < 5%              # Coordination performance
rate_limit.instance_count = 4               # Active instances coordinating
rate_limit.fair_distribution = true         # Equal token allocation
```

### Multi-Instance Coordination Examples

**Normal Operations (4 Instances):**
```
🎯 WebApp-1: Allocated 150 tokens
🎯 WebApp-2: Allocated 150 tokens  
🎯 Worker-1: Allocated 150 tokens
🎯 Worker-2: Allocated 150 tokens
Total: 600 tokens (100% utilization of safe quota)
```

**Auto-Scaling Scenario:**
```
Before scaling (2 instances):
🎯 WebApp-1: 300 tokens
🎯 WebApp-2: 300 tokens

After scaling to 4 instances:
🎯 WebApp-1: 150 tokens (automatic rebalancing)
🎯 WebApp-2: 150 tokens (automatic rebalancing)
🎯 WebApp-3: 150 tokens (new instance)
🎯 WebApp-4: 150 tokens (new instance)
```

**Instance Failure Recovery:**
```
Before failure:
🎯 WebApp-1: 150 tokens
🎯 WebApp-2: 150 tokens
🎯 Worker-1: 150 tokens

After WebApp-2 crashes:
🎯 WebApp-1: 225 tokens (+50% increase)
🎯 Worker-1: 225 tokens (+50% increase)
💥 WebApp-2: Failed (tokens redistributed)
```

**Critical Quota Protection:**
```
When quota drops to 50 requests remaining:
🎯 WebApp-1: 8 tokens (severely limited)
🎯 WebApp-2: 8 tokens (severely limited)
🎯 Worker-1: 8 tokens (severely limited)
Total: 24 tokens (50% safety buffer in critical mode)
```

---

## 📋 Configuration Cheat Sheet

### Essential Settings (Enhanced Integration)
```json
{
  "DynamicRateLimiting": {
    "Features": {
      "EnablePredictiveDistribution": false,   // Master toggle - NEW
      "EnableInstanceCoordination": true,      // Multi-instance coordination - NEW  
      "EnableQuotaTracking": true,             // Real-time quota intelligence - NEW
      "EnableCircuitBreaker": true             // Fallback protection - NEW
    },
    "PredictiveSettings": {                    // NEW section
      "SafetyBufferPercentage": 0.15,         // 15% normal, 25% warning, 50% critical
      "HealthyQuotaThreshold": 0.3,           // >30% remaining = healthy
      "CriticalQuotaThreshold": 0.1,          // <10% remaining = critical
      "InstanceTimeoutSeconds": 90,           // Aggressive phantom cleanup
      "MaxETagRetries": 5                     // ETag conflict resolution
    },
    "TableStorage": {                          // ENHANCED existing
      "QuotaTableName": "RateLimitQuotas",    // Real-time quota state
      "InstanceTableName": "RateLimitInstances", // Instance coordination
      "TokenTableName": "RateLimitTokens",    // Distributed allocation
      "AutoCreateTables": true                // Follow existing patterns
    }
  }
}
```

### Safety Buffer Rules
| Quota Health | Remaining % | Safety Buffer | Available % |
|--------------|-------------|---------------|-------------|
| **Healthy** | >30% | 15% | 85% |
| **Warning** | 10-30% | 25% | 75% |
| **Critical** | <10% | 50% | 50% |

---

## 🔧 Common Operations

### Check Quota Status
```csharp
var health = await quotaTracker.GetQuotaHealthAsync("store-12345");
Console.WriteLine($"Health: {health.HealthStatus}");
Console.WriteLine($"Utilization: {health.QuotaUtilizationPercent:F1}%");
Console.WriteLine($"Safe Tokens: {health.SafeTokens}");
```

### Manual Token Reservation
```csharp
var tokenManager = serviceProvider.GetService<IPredictiveTokenManager>();
var tokens = await tokenManager.ReserveTokensAsync("store-12345");
Console.WriteLine($"Reserved {tokens} tokens");
```

### Force Quota Refresh
```csharp
// Headers are parsed automatically, but you can force update:
var quotaTracker = serviceProvider.GetService<IQuotaTracker>();
await quotaTracker.UpdateQuotaFromHeadersAsync(storeId, response);
```

---

## 🔍 Debugging Guide

### Check Feature Status
```csharp
// Verify which rate limiter is active
var rateLimiter = serviceProvider.GetService<IRateLimitService>();
var isDistributed = rateLimiter is DistributedRateLimitService;
Console.WriteLine($"Using distributed limiter: {isDistributed}");
```

### Table Storage Inspection
```sql
-- Query quota data directly
SELECT PartitionKey, RemainingTokens, CurrentQuota, LastUpdated 
FROM RateLimitQuotas 
WHERE PartitionKey = 'store-12345'

-- Check active instances
SELECT PartitionKey, RowKey, LastHeartbeat 
FROM RateLimitInstances 
WHERE PartitionKey = 'store-12345'
```

### Common Log Messages
```bash
# Success indicators
"DistributedQuotaTracker initialized with table: RateLimitQuotas"
"Parsed rate limit headers for store store-12345: 387/500 remaining"
"Successfully reserved 25 tokens for instance web-01-a1b2c3d4"

# Warning indicators  
"Throttling quota update for store store-12345 (too frequent)"
"ETag conflict updating quota for store store-12345, retry 2/5"

# Error indicators
"Failed to update quota from headers for store store-12345"
"Failed to reserve tokens after 5 retries due to ETag conflicts"
```

---

## 📊 Monitoring Checklist

### Key Metrics to Watch
```yaml
Critical Metrics:
  - rate_limit.429_rate: 0%              # Zero tolerance
  - rate_limit.decisions: distributed     # Confirm usage
  
Performance Metrics:
  - rate_limit.quota_utilization: >90%   # Efficiency target
  - rate_limit.token_efficiency: >95%    # Waste indicator
  - rate_limit.reservation_latency: <50ms # Speed target
  
Health Metrics:
  - rate_limit.etag_conflicts: <5%       # Contention indicator
  - rate_limit.instance_count: actual    # Coordination check
  - rate_limit.quota_freshness: <30s     # Data staleness
```

### Health Check Endpoints
```bash
# Application health
GET /health/quota-tracker
# Response: {"status": "Healthy", "table_storage_connected": true}

# Detailed quota status  
GET /api/debug/quota/{storeId}
# Response: quota utilization, token status, instance count
```

---

## 🚨 Troubleshooting

### Problem: High 429 Error Rate
**Symptoms**: `rate_limit.429_rate > 0%`
```bash
# Check if distributed limiter is actually enabled
grep "UseDistributedLimiter.*true" appsettings.json

# Verify quota tracking is working
grep "Parsed rate limit headers" logs.txt

# Check safety buffer settings
grep "SafetyBufferPercentage" appsettings.json
```

**Solutions**:
1. Increase safety buffer to 25% temporarily
2. Verify headers are being parsed correctly
3. Check for ETag conflict storms

### Problem: Low Quota Utilization
**Symptoms**: `rate_limit.quota_utilization < 80%`
```bash
# Check token distribution
grep "Reserved.*tokens" logs.txt

# Check instance coordination
grep "active instances" logs.txt
```

**Solutions**:
1. Reduce safety buffer to 10% if quota is stable
2. Check for stale instance registrations
3. Verify token expiry settings

### Problem: High ETag Conflicts  
**Symptoms**: `rate_limit.etag_conflicts > 10%`
```bash
# Check concurrency levels
grep "ETag conflict.*retry" logs.txt

# Check instance count
grep "active instances" logs.txt
```

**Solutions**:
1. Increase jitter in retry delays
2. Reduce token reservation frequency
3. Implement backoff for high-conflict stores

### Problem: Table Storage Errors
**Symptoms**: Connection failures, timeout errors
```bash
# Test connectivity
az storage table list --connection-string "..."

# Check table existence
az storage table exists --name RateLimitQuotas
```

**Solutions**:
1. Verify connection string is correct
2. Check Azure Storage account health
3. Enable auto-table creation
4. Verify firewall/network access

---

## 🔄 Operational Runbook

### Daily Operations
```bash
# Morning health check
curl /health/quota-tracker

# Check 429 rate from overnight
grep "429" logs-$(date -d yesterday +%Y%m%d).txt | wc -l

# Verify quota utilization
grep "quota_utilization" metrics-$(date +%Y%m%d).json
```

### Weekly Review
```sql
-- Top stores by token consumption
SELECT PartitionKey, SUM(TotalTokensConsumed) as Total
FROM RateLimitInstances 
WHERE LastHeartbeat > dateadd(day, -7, getdate())
GROUP BY PartitionKey 
ORDER BY Total DESC

-- ETag conflict hot spots
SELECT PartitionKey, AVG(ETagConflictCount) as AvgConflicts
FROM RateLimitTokens
GROUP BY PartitionKey
HAVING AVG(ETagConflictCount) > 10
```

### Emergency Procedures

#### Immediate Rollback
```json
{
  "PredictiveRateLimiting": {
    "Features": {
      "UseDistributedLimiter": false  // Instant fallback
    }
  }
}
```

#### Partial Rollback (Per Store)
```json
{
  "PredictiveRateLimiting": {
    "ExcludedStores": ["store-problem-1", "store-problem-2"]
  }
}
```

#### Circuit Breaker Trigger
```csharp
// Automatically falls back on consecutive failures
// Check circuit breaker status:
var healthCheck = await quotaTrackerHealthCheck.CheckHealthAsync();
Console.WriteLine($"Circuit breaker open: {healthCheck.Status == Unhealthy}");
```

---

## 📚 API Reference

### IQuotaTracker Interface
```csharp
// Get current quota for a store
Task<StoreQuotaEntity> GetCurrentQuotaAsync(string storeId);

// Update quota from API response headers  
Task UpdateQuotaFromHeadersAsync(string storeId, HttpResponseMessage response);

// Get health metrics for monitoring
Task<QuotaHealthMetrics> GetQuotaHealthAsync(string storeId);
```

### IPredictiveTokenManager Interface
```csharp
// Reserve tokens for current instance
Task<int> ReserveTokensAsync(string storeId);

// Check token availability without reserving
Task<int> GetAvailableTokensAsync(string storeId);

// Release unused tokens back to pool
Task ReleaseTokensAsync(string storeId, int tokens);
```

### Configuration Sections
```csharp
// Access configuration in code
services.Configure<PredictiveRateLimitingConfiguration>(
    configuration.GetSection("PredictiveRateLimiting"));

// Feature flags
bool useDistributed = config.Features.UseDistributedLimiter;
bool parseHeaders = config.Features.EnableHeaderParsing;

// Safety settings
double safetyBuffer = config.Safety.SafetyBufferPercentage;
double healthyThreshold = config.Safety.HealthyQuotaThreshold;
```

---

## 🎯 Performance Targets

### Response Time Targets
- Token reservation: <50ms p95
- Quota lookup: <20ms p95  
- Header parsing: <5ms p95
- Instance heartbeat: <100ms p95

### Efficiency Targets
- Quota utilization: >90%
- Token efficiency: >95% (reserved vs used)
- ETag conflict rate: <5%
- 429 error rate: 0%

### Scalability Targets
- Support 1000+ stores simultaneously
- Handle 100+ instances per store
- Process 10000+ API calls per minute
- Maintain <1MB Table Storage per store per day

This quick reference provides immediate access to the most commonly needed information for operating and troubleshooting the predictive rate limiting system.
