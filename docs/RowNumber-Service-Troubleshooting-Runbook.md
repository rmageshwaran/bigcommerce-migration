# 🛠️ RowNumber Service - Production Troubleshooting Runbook

**Service**: `BigCommerce.Migration.Infrastructure.Services.RowNumberService`  
**Purpose**: Operational guide for monitoring and troubleshooting RowNumber service  
**Audience**: DevOps, SRE, Development Teams  
**Last Updated**: January 29, 2025

---

# 🚨 **CRITICAL ISSUES & RESOLUTION**

## **Issue 1: High Latency (>1000ms average)**

### **Symptoms:**
- Application Insights shows P95 latency >1000ms
- Users reporting slow migration performance
- Logs show extended retry cycles

### **Root Causes:**
1. **High Concurrency Contention**: >20 simultaneous RowNumber requests
2. **Azure Table Storage Throttling**: 429 responses from Azure
3. **Network Connectivity**: Intermittent connection issues

### **Resolution Steps:**

#### **Step 1: Check Concurrency Levels**
```kusto
// Application Insights query
traces
| where message contains "ROWNUMBER"
| where timestamp >= ago(1h)
| summarize RequestCount = count() by bin(timestamp, 1m)
| render timechart
```

**Action**: If >150 requests/minute consistently:
- Consider using `AllocateRangeAsync()` for batch operations instead of individual assignments
- Reduce concurrent batch processing if possible

#### **Step 2: Check Azure Table Storage Health**
```kusto
// Check for throttling
exceptions
| where outerMessage contains "429" or outerMessage contains "throttl"
| where timestamp >= ago(1h)  
| summarize count() by bin(timestamp, 5m)
```

**Action**: If throttling detected:
- Contact Azure support for Table Storage scaling
- Consider implementing additional backoff logic

#### **Step 3: Network Connectivity Check**
```bash
# Check connectivity to Azure Table Storage
nslookup <your-storage-account>.table.core.windows.net
curl -I https://<your-storage-account>.table.core.windows.net
```

**Action**: If connectivity issues:
- Check network configuration and firewall rules
- Verify Azure Storage account accessibility

---

## **Issue 2: Low Success Rate (<90%)**

### **Symptoms:**
- Application Insights shows success rate dropping below 90%
- Increasing retry counts in logs
- Migration progression slowing down

### **Root Causes:**
1. **Excessive Concurrent Load**: Too many simultaneous requests
2. **Azure Table Storage Overload**: Service experiencing high load
3. **ETag Conflict Rate**: High conflict rate due to concurrent access

### **Resolution Steps:**

#### **Step 1: Analyze Conflict Patterns**
```kusto
// Concurrency conflict analysis
traces
| where message contains "Concurrency conflict"
| extend MigrationId = extract("for ([^/]+)/", 1, message)
| extend Attempt = extract("#(\\d+)", 1, message)
| summarize 
    ConflictCount = count(),
    MaxAttempt = max(toint(Attempt))
    by MigrationId, bin(timestamp, 5m)
| render timechart
```

#### **Step 2: Implement Load Balancing**
```csharp
// Recommendation: Use range allocation for high-volume operations
if (batchSize >= 10)
{
    // ✅ RECOMMENDED: Single range allocation
    var (start, end) = await _rowNumberService.AllocateRangeAsync(migrationId, entityType, batchSize);
}
else
{
    // Individual assignments for small batches
    var rowNumber = await _rowNumberService.GetNextRowNumberAsync(migrationId, entityType);
}
```

#### **Step 3: Temporary Concurrency Reduction**
- Reduce `maxConcurrency` in configuration by 50%
- Monitor success rate improvement
- Gradually increase concurrency back to optimal levels

---

## **Issue 3: Service Unavailable Errors**

### **Symptoms:**
- `InvalidOperationException: Failed to initialize table RowNumberCounters`
- Connection refused errors (127.0.0.1:10002 for Azurite)
- Azure Table Storage connectivity failures

### **Root Causes:**
1. **Azure Storage Account Issues**: Service outage or configuration
2. **Network Connectivity**: Firewall or DNS issues
3. **Connection String**: Invalid or expired credentials

### **Resolution Steps:**

#### **Step 1: Validate Azure Storage Account**
```bash
# Check storage account status
az storage account show --name <storage-account> --resource-group <rg>

# Test connection with Azure CLI
az storage table list --account-name <storage-account>
```

#### **Step 2: Verify Connection Strings**
```csharp
// Connection string validation
var connectionString = configuration["AzureWebJobsStorage"];
// Ensure connection string is valid and accessible
```

#### **Step 3: Health Check Endpoint**
```csharp
// Use built-in health check
var healthResult = await _rowNumberService.HealthCheckAsync();
if (!healthResult)
{
    // Service is unhealthy - check logs for details
}
```

---

# 📊 **PERFORMANCE MONITORING**

## **Key Performance Indicators:**

### **Service Health Metrics:**
- **Requests per Second**: Target >50, Alert <25  
- **Average Latency**: Target <100ms, Alert >500ms
- **P95 Latency**: Target <200ms, Alert >1000ms
- **Success Rate**: Target >95%, Alert <90%

### **Azure Table Storage Metrics:**
- **Connection Count**: Monitor connection pool usage
- **Request Timeouts**: Target <1%, Alert >5%
- **Throttling Events**: Target 0, Alert >5/hour

## **Monitoring Dashboard Queries:**

### **Real-Time Performance:**
```kusto
customEvents
| where name contains "ROWNUMBER"
| where timestamp >= ago(1h)
| extend Latency = todouble(customDimensions.ElapsedMs)
| summarize 
    RequestCount = count(),
    AvgLatency = avg(Latency),
    P95Latency = percentile(Latency, 95),
    MaxLatency = max(Latency)
    by bin(timestamp, 5m)
| render timechart
```

### **Success Rate Trends:**
```kusto
traces 
| where message contains "ROWNUMBER-METRICS"
| extend Success = message contains "SUCCESS"
| summarize 
    SuccessRate = 100.0 * countif(Success) / count()
    by bin(timestamp, 10m)
| render timechart
```

---

# 🔧 **CONFIGURATION TUNING**

## **High-Traffic Scenarios:**

### **Increase Retry Resilience:**
```json
{
  "RowNumberService": {
    "MaxRetryAttempts": 10,        // For very high concurrency
    "BaseRetryDelayMs": 25,        // Faster initial retries
    "MaxRetryDelayMs": 2000        // Higher cap for extreme contention
  }
}
```

### **Optimize for Speed:**
```json
{
  "RowNumberService": {
    "MaxRetryAttempts": 5,         // Fewer retries for faster response
    "BaseRetryDelayMs": 100,       // Standard timing
    "MaxRetryDelayMs": 500         // Quick failure detection
  }
}
```

## **Load-Specific Recommendations:**

### **Low Load (<1000 entities/migration):**
- Individual assignments acceptable
- Standard retry settings
- Basic monitoring

### **Medium Load (1K-100K entities):**
- Prefer range allocation for batches ≥10
- Standard production settings  
- Active performance monitoring

### **High Load (>100K entities):**
- Always use range allocation
- Enhanced retry settings
- Real-time monitoring with alerts

---

# 🆘 **EMERGENCY PROCEDURES**

## **Service Degradation Response:**

### **Step 1: Immediate Assessment**
1. Check Application Insights for error patterns
2. Validate Azure Storage account status
3. Verify network connectivity

### **Step 2: Temporary Mitigation**
1. Reduce migration concurrency by 50%
2. Enable verbose logging for troubleshooting
3. Switch to smaller batch sizes if needed

### **Step 3: Escalation Path**
1. **Level 1**: Development team (configuration issues)
2. **Level 2**: Infrastructure team (Azure Storage issues)
3. **Level 3**: Microsoft Azure support (platform issues)

## **Recovery Validation:**
After implementing fixes:
1. Monitor success rate return to >95%
2. Verify latency back to <100ms average
3. Confirm no data integrity issues
4. Gradually restore normal concurrency levels

---

# 📞 **SUPPORT CONTACTS**

## **Internal Teams:**
- **Development Team**: RowNumber service code and configuration
- **DevOps Team**: Azure infrastructure and monitoring
- **QA Team**: Performance testing and validation

## **External Support:**
- **Microsoft Azure**: Storage account and Table Storage issues
- **Application Insights**: Monitoring and alerting configuration

---

**This runbook provides comprehensive guidance for maintaining optimal RowNumber service performance in production environments.**
