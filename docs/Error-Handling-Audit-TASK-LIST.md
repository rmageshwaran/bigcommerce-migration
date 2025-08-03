# 🚨 **Error Handling Audit & Improvement Task List**
## *Systematic Implementation of OpenSearch Logging Standards*

### 📋 **Overview**
This task list implements the mandatory error handling standards across the BigCommerce Migration system to ensure:
1. **100% OpenSearch logging coverage** for all exceptions
2. **Consistent metadata structure** across all services  
3. **Proper infrastructure vs application error distinction**
4. **No silent exception suppressions**

---

## 🎯 **TASK 1: Audit Existing Catch Blocks**
**Status**: 🔄 In Progress | **Priority**: Critical | **Estimated**: 4-6 hours

### **1.1 Identify All Catch Blocks** *(1 hour)*
- [ ] **Run comprehensive grep search** for all `catch` statements
- [ ] **Categorize by file type**: Services, Activities, Orchestrators, Functions
- [ ] **Create audit spreadsheet** with columns:
  - File path
  - Line number  
  - Current logging method (Console/OpenSearch/None)
  - Exception type
  - Infrastructure vs Application
  - Action needed

### **1.2 Console-Only Logging Analysis** *(2 hours)*
**Files with console-only logging patterns to review:**

#### **🔥 CRITICAL - Services (Infrastructure)**
- [ ] `src/BigCommerce.Migration.Functions/Services/ApiKeyService.cs` (Lines: 94, 148, 175, 206)
- [ ] `src/BigCommerce.Migration.Functions/Services/ApiRateLimitService.cs` (Line: 172)
- [ ] `src/BigCommerce.Migration.Infrastructure/Services/BatchApiClient.cs` (Lines: 71, 116, 161, 208, 255, 320)
- [ ] `src/BigCommerce.Migration.Infrastructure/Services/PaginationApiService.cs` (Line: 88)

#### **⚡ HIGH - Orchestration Services**
- [ ] `src/BigCommerce.Migration.Orchestration/Services/EntityFetchService.cs` (Lines: 85, 91, 133, 246)
- [ ] `src/BigCommerce.Migration.Orchestration/Services/EntityMappingService.cs` (Lines: 60, 88, 113, 146, 190, 222, 255)
- [ ] `src/BigCommerce.Migration.Orchestration/Services/EntityTransformService.cs` (Line: 70)
- [ ] `src/BigCommerce.Migration.Orchestration/Services/ProgressTracker.cs` (Lines: 74, 159, 587, 595, 644, 682, 723)

#### **🔧 MEDIUM - Activities & Functions**
- [ ] `src/BigCommerce.Migration.Orchestration/Activities/DiscoverEntitiesActivity.cs` (Lines: 70, 83)
- [ ] `src/BigCommerce.Migration.Functions/Functions/MigrationQueueFunctions.cs` (Lines: 63, 140, 185, 325, 421, 445, 514, 542, 560, 578)
- [ ] `src/BigCommerce.Migration.Functions/Functions/MigrationHttpFunctions.cs` (Lines: 199, 256, 386, 453, 536, 619, 727, 814, 1268, 1620, 1636, 1645, 1816)

### **1.3 OpenSearch Integration Assessment** *(1-2 hours)*
- [ ] **Verify OpenSearch service availability** in each component
- [ ] **Check DI registrations** for `IOpenSearchService`
- [ ] **Identify missing service injections** in constructors
- [ ] **Review existing OpenSearch logging patterns** for consistency

---

## 🏗️ **TASK 2: Standardize Metadata Fields**
**Status**: ⏳ Pending | **Priority**: High | **Estimated**: 3-4 hours

### **2.1 Define Standard Metadata Structure** *(1 hour)*
Create consistent metadata schema:
```csharp
// STANDARD METADATA STRUCTURE
new { 
    migrationId,           // String - For correlation (REQUIRED)
    entityType,            // String - Entity being processed (when applicable)
    errorType,             // String - Categorized error type (REQUIRED)
    severity,              // String - Critical/Error/Warning (REQUIRED)
    component,             // String - Service/Activity name (REQUIRED)
    operationContext,      // String - What operation was being performed
    retryable = false,     // Boolean - Whether error supports retry
    httpStatusCode,        // Integer - For API errors (when applicable)
    batchNumber,           // Integer - For batch operations (when applicable)
    entityId,              // String - Specific entity ID (when applicable)
    timestamp = DateTime.UtcNow  // DateTime - When error occurred
}
```

### **2.2 Update Existing OpenSearch Calls** *(2-3 hours)*
- [ ] **EntityErrorHandlingService.cs** - Standardize metadata structure
- [ ] **ChunkedErrorHandlingService.cs** - Align with standard schema
- [ ] **Update all services** currently using OpenSearch to use standard metadata
- [ ] **Create helper extension method** for consistent metadata creation

### **2.3 Create Metadata Helper** *(30 minutes)*
```csharp
// src/BigCommerce.Migration.Core/Extensions/ErrorLoggingExtensions.cs
public static class ErrorLoggingExtensions
{
    public static object CreateStandardErrorMetadata(
        string component,
        string? migrationId = null,
        string? entityType = null,
        string? operationContext = null,
        Exception? exception = null,
        int? batchNumber = null,
        string? entityId = null)
    {
        return new {
            migrationId,
            entityType,
            errorType = CategorizeErrorType(exception),
            severity = DetermineSeverity(exception),
            component,
            operationContext,
            retryable = IsRetryable(exception),
            httpStatusCode = ExtractHttpStatusCode(exception),
            batchNumber,
            entityId,
            timestamp = DateTime.UtcNow
        };
    }
}
```

---

## 🔇 **TASK 3: Fix Silent Suppressions**
**Status**: ⏳ Pending | **Priority**: Critical | **Estimated**: 2-3 hours

### **3.1 Identify Silent Catch Blocks** *(1 hour)*
Search patterns for silent suppressions:
```bash
# Find catch blocks that might be suppressing silently
grep -n "catch.*{" src/**/*.cs | grep -v "LogError\|LogWarning\|LogCritical\|openSearchService"
```

### **3.2 Priority Silent Suppression Fixes** *(2 hours)*

#### **🚨 CRITICAL - Infrastructure Services**
- [ ] `src/BigCommerce.Migration.Infrastructure/Services/OpenSearchService.cs`
  - Lines: 78, 127, 220, 282, 337, 465, 489, 551
  - **Issue**: OpenSearch logging failures are suppressed
  - **Fix**: Add fallback logging mechanism

#### **⚠️ HIGH - Error Recovery Services**  
- [ ] `src/BigCommerce.Migration.Orchestration/Services/ChunkedErrorRecoveryService.cs`
  - Lines: 198, 316, 363
  - **Issue**: Recovery failures not logged to OpenSearch
  - **Fix**: Add structured error logging

#### **🔧 MEDIUM - Cleanup Services**
- [ ] `src/BigCommerce.Migration.Orchestration/Services/OrchestratorCleanupService.cs`
  - Lines: 116, 128, 165, 189, 220, 299, 372
  - **Issue**: Cleanup failures only console logged
  - **Fix**: Add OpenSearch logging with cleanup context

### **3.3 Add Comprehensive Logging** *(1 hour)*
For each silent catch block:
```csharp
// BEFORE (Silent suppression)
catch (Exception ex)
{
    // Silent or minimal logging
}

// AFTER (Proper OpenSearch logging)
catch (Exception ex)
{
    _logger.LogError(ex, "Operation failed: {OperationName}", operationName);
    
    await _openSearchService.LogErrorAsync(
        $"{nameof(ServiceName)}.{nameof(MethodName)}", 
        ex, 
        ErrorLoggingExtensions.CreateStandardErrorMetadata(
            component: nameof(ServiceName),
            migrationId: migrationId,
            operationContext: "detailed context"
        )
    );
    
    // Return safe default or proper error result
}
```

---

## 🏗️ **TASK 4: Infrastructure Error Review**  
**Status**: ⏳ Pending | **Priority**: Critical | **Estimated**: 3-4 hours

### **4.1 Infrastructure Exception Classification** *(1 hour)*
Review and categorize all infrastructure-related catch blocks:

#### **✅ CORRECTLY THROWING** (Should throw)
- [ ] **Storage Services**: Azure Table Storage, Blob Storage connections
- [ ] **API Authentication**: BigCommerce API key failures  
- [ ] **Network Infrastructure**: DNS, socket failures
- [ ] **Azure Services**: Functions runtime, SignalR connection issues

#### **❌ INCORRECTLY SUPPRESSING** (Need to throw)
- [ ] `src/BigCommerce.Migration.Infrastructure/Services/OpenSearchService.cs` - OpenSearch connection failures
- [ ] `src/BigCommerce.Migration.Infrastructure/Services/BatchApiClient.cs` - API connectivity issues
- [ ] Storage service connection failures being suppressed

### **4.2 Update Infrastructure Error Handling** *(2 hours)*
Apply infrastructure exception patterns:

#### **Update OpenSearchService** *(30 minutes)*
```csharp
// src/BigCommerce.Migration.Infrastructure/Services/OpenSearchService.cs
catch (Exception ex)
{
    if (IsInfrastructureException(ex))
    {
        _logger.LogCritical("🚨 OpenSearch infrastructure failure: {Error}", ex.Message);
        throw; // Critical infrastructure - must bubble up
    }
    
    _logger.LogWarning("OpenSearch operation failed gracefully: {Error}", ex.Message);
    return false; // Application-level failure - continue
}
```

#### **Update API Services** *(1 hour)*
- [ ] **BatchApiClient.cs** - Distinguish network vs API response errors
- [ ] **ApiRequestHandler.cs** - Proper infrastructure exception handling
- [ ] **PaginationApiService.cs** - Network failure detection

#### **Update Storage Services** *(30 minutes)*  
- [ ] **Migration storage services** - Connection vs data errors
- [ ] **Blob storage operations** - Infrastructure vs content errors

### **4.3 Infrastructure Exception Helper Updates** *(1 hour)*
Enhance the `IsInfrastructureException` helper:
```csharp
private static bool IsInfrastructureException(Exception ex)
{
    return ex.Message.Contains("Storage") ||
           ex.Message.Contains("unavailable") ||
           ex.Message.Contains("timeout") ||
           ex.Message.Contains("connection") ||
           ex.Message.Contains("network") ||
           ex.Message.Contains("authentication") ||
           ex.Message.Contains("unauthorized") ||
           ex.Message.Contains("service") ||
           ex is TimeoutException ||
           ex is HttpRequestException httpEx && IsInfrastructureHttpError(httpEx) ||
           ex is SocketException ||
           ex is UnauthorizedAccessException ||
           ex is InvalidOperationException && ex.Message.Contains("storage");
}

private static bool IsInfrastructureHttpError(HttpRequestException httpEx)
{
    // Extract status code and check if it's infrastructure-related
    if (httpEx.Data.Contains("StatusCode"))
    {
        var statusCode = httpEx.Data["StatusCode"]?.ToString();
        return statusCode is "401" or "403" or "503" or "504" or "429";
    }
    return false;
}
```

---

## ✅ **TASK 5: Validation & Testing**
**Status**: ⏳ Pending | **Priority**: Medium | **Estimated**: 2-3 hours

### **5.1 Create Error Handling Tests** *(2 hours)*
- [ ] **Unit tests** for infrastructure vs application error distinction
- [ ] **Integration tests** for OpenSearch logging functionality  
- [ ] **Mock failure scenarios** to verify proper error handling
- [ ] **Test metadata consistency** across different services

### **5.2 Documentation Updates** *(1 hour)*
- [ ] **Update code comments** with error handling expectations
- [ ] **Create troubleshooting guide** for error investigation
- [ ] **Document OpenSearch query patterns** for error analysis

---

## 📊 **IMPLEMENTATION PRIORITY ORDER**

### **Phase 1 (Critical - Week 1)**
1. **Task 3**: Fix Silent Suppressions - Infrastructure services
2. **Task 4**: Infrastructure Error Review - Critical failure handling  
3. **Task 1.2**: Console-Only Logging - Infrastructure services

### **Phase 2 (High - Week 2)** 
1. **Task 1.2**: Console-Only Logging - Orchestration services
2. **Task 2**: Standardize Metadata Fields
3. **Task 3**: Fix Silent Suppressions - Non-infrastructure services

### **Phase 3 (Medium - Week 3)**
1. **Task 1.2**: Console-Only Logging - Activities & Functions
2. **Task 5**: Validation & Testing
3. **Final audit and documentation**

---

## 🎯 **SUCCESS METRICS**

- [ ] **100% catch block coverage** with proper logging
- [ ] **Zero silent exception suppressions** 
- [ ] **Consistent metadata structure** across all OpenSearch logs
- [ ] **Proper infrastructure vs application error distinction**
- [ ] **All tests passing** with new error handling patterns
- [ ] **OpenSearch dashboards** showing structured error data

---

## 🔍 **VALIDATION COMMANDS**

```bash
# Verify no silent catch blocks remain
grep -r "catch.*Exception.*{" src/ | grep -v "LogError\|LogWarning\|LogCritical\|openSearchService"

# Check OpenSearch service injection
grep -r "IOpenSearchService" src/ --include="*.cs" | grep "constructor\|DI"

# Verify infrastructure exception handling
grep -r "IsInfrastructureException\|throw.*infrastructure" src/ --include="*.cs"

# Check metadata standardization  
grep -r "LogErrorAsync" src/ --include="*.cs" -A 5 -B 5
```

This comprehensive task list ensures systematic implementation of proper error handling standards across the entire BigCommerce Migration system! 🚀