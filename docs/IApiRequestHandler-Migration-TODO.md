# 🚨 **IApiRequestHandler Migration - Comprehensive TODO**

## 🎯 **Critical Architectural Constraint**

**ALL API calls must be routed through IApiRequestHandler to ensure consistent rate limiting, logging, and error handling.**

**Current Status**: Only creation strategies (variants, etc.) use IApiRequestHandler correctly. Discovery strategies, activities, and services still use IBigCommerceApiClient directly, bypassing rate limiting.

---

## 📊 **Analysis Summary**

**Total Files Requiring Updates**: **17 files**  
**Total API Call Locations**: **25+ locations**  
**Priority**: **Critical for production reliability**

---

## 🔍 **CATEGORY 1: DISCOVERY STRATEGIES** 
**Priority**: **HIGH** - These run frequently during migration discovery phase

### **Files to Update:**

1. ✅ **`ProductMetafieldsDiscoveryStrategy.cs`** - **COMPLETED**
   - **Status**: Updated to use IApiRequestHandler
   - **Pattern**: Direct API URL building + ApiRequest.CreateGet()

2. **`V3EfficientPaginationStrategy.cs`** - **TODO**
   - **Location**: Line ~77: `await _apiClient.GetPaginatedEntitiesAsync()`
   - **Impact**: **HIGH** - Used by most entity types (products, brands, variants)
   - **Pattern**: Replace with `_apiRequestHandler.ExecuteRequestAsync()` + manual URL building
   - **Estimated Effort**: 4-6 hours
   - **Dependencies**: Update constructor, DI registration, all consumers

3. **`V3HierarchicalStrategy.cs`** - **TODO** 
   - **Location**: Line ~80: `await _apiClient.GetPaginatedEntitiesAsync()`
   - **Impact**: **MEDIUM** - Used only by categories
   - **Pattern**: Replace with `_apiRequestHandler.ExecuteRequestAsync()` + manual URL building
   - **Estimated Effort**: 2-3 hours
   - **Dependencies**: Update constructor, DI registration

---

## 🔍 **CATEGORY 2: FETCH STRATEGIES**
**Priority**: **HIGH** - These run during entity fetching phase

### **Files to Update:**

4. **`CategoryFetchStrategy.cs`** - **TODO**
   - **Location**: Line ~73: `await _apiClient.GetPaginatedEntitiesAsync()`
   - **Impact**: **MEDIUM** - Category fetching operations
   - **Pattern**: Replace with `_apiRequestHandler.ExecuteRequestAsync()`
   - **Estimated Effort**: 2-3 hours

5. **`FetchEntityPageActivity.cs`** - **TODO**
   - **Location**: Line ~137: `await _apiClient.GetPaginatedEntitiesAsync()`
   - **Impact**: **HIGH** - All entity page fetching operations
   - **Pattern**: Replace with `_apiRequestHandler.ExecuteRequestAsync()`
   - **Estimated Effort**: 3-4 hours

6. **`EntityFetchService.cs`** - **TODO**
   - **Locations**: 
     - Line ~462: `await _apiClient.GetPaginatedEntitiesAsync()`
     - Line ~660: `await _apiClient.GetPaginatedEntitiesAsync()`
     - Line ~774: `await _apiClient.GetProductVariantsAsync()`
     - Line ~803: `await _apiClient.GetProductImagesAsync()`
     - Line ~832: `await _apiClient.GetProductModifiersAsync()`
   - **Impact**: **CRITICAL** - All entity fetching operations
   - **Pattern**: Replace all methods with `_apiRequestHandler.ExecuteRequestAsync()`
   - **Estimated Effort**: 6-8 hours
   - **Complexity**: High - multiple API endpoints

---

## 🔍 **CATEGORY 3: CREATION STRATEGIES** 
**Priority**: **MEDIUM** - Some still use IBigCommerceApiClient inconsistently

### **Files to Update:**

7. **`CategoryCreationStrategy.cs`** - **TODO**
   - **Location**: Line ~125: `await _apiClient.CreateCategoriesAsync()`
   - **Impact**: **MEDIUM** - Category creation operations
   - **Current Status**: Inconsistent with variants pattern
   - **Pattern**: Update to match VariantCreationStrategy pattern
   - **Estimated Effort**: 3-4 hours

8. **`ProductCreationStrategy.cs`** - **TODO**
   - **Dependency**: Still injects `IBigCommerceApiClient`
   - **Impact**: **MEDIUM** - Product creation operations  
   - **Current Status**: Inconsistent with variants pattern
   - **Pattern**: Update constructor and API calls
   - **Estimated Effort**: 3-4 hours

9. **`ModifierCreationStrategy.cs`** - **TODO**
   - **Dependency**: Still injects `IBigCommerceApiClient`
   - **Impact**: **MEDIUM** - Modifier creation operations
   - **Current Status**: Inconsistent with variants pattern
   - **Pattern**: Update constructor and API calls
   - **Estimated Effort**: 2-3 hours

10. **`ReviewsCreationStrategy.cs`** - **TODO**
    - **Dependency**: Still injects `IBigCommerceApiClient`
    - **Impact**: **MEDIUM** - Reviews creation operations
    - **Current Status**: Inconsistent with variants pattern
    - **Pattern**: Update constructor and API calls
    - **Estimated Effort**: 2-3 hours

11. **`ImageCreationStrategy.cs`** - **TODO**
    - **Dependency**: Still injects `IBigCommerceApiClient`
    - **Impact**: **MEDIUM** - Image creation operations
    - **Current Status**: Inconsistent with variants pattern
    - **Pattern**: Update constructor and API calls
    - **Estimated Effort**: 2-3 hours

12. **`OptionsCreationStrategy.cs`** - **TODO**
    - **Dependency**: Still injects `IBigCommerceApiClient`
    - **Impact**: **MEDIUM** - Options creation operations
    - **Current Status**: Inconsistent with variants pattern
    - **Pattern**: Update constructor and API calls
    - **Estimated Effort**: 2-3 hours

---

## 🔍 **CATEGORY 4: ACTIVITIES**
**Priority**: **HIGH** - Used for validation and critical operations

### **Files to Update:**

13. **`ValidateMigrationStoresActivity.cs`** - **TODO**
    - **Locations**: 
      - Line ~40: `await _apiClient.IsHealthyAsync()`
      - Line ~52: `await _apiClient.IsHealthyAsync()`
    - **Impact**: **HIGH** - Store validation operations (migration startup)
    - **Pattern**: Replace with health check API calls via IApiRequestHandler
    - **Estimated Effort**: 2-3 hours

14. **`ResolveCategoryTreeIdsActivity.cs`** - **TODO**
    - **Location**: Line ~124: `await _apiClient.GetCategoryTreesAsync()`
    - **Impact**: **HIGH** - Category tree resolution (migration startup)
    - **Pattern**: Replace with `/v3/catalog/trees` API call via IApiRequestHandler
    - **Estimated Effort**: 3-4 hours

---

## 🔍 **CATEGORY 5: SERVICES**
**Priority**: **MEDIUM** - Infrastructure services

### **Files to Update:**

15. **`CategoryTreeResolver.cs`** - **TODO**
    - **Locations**:
      - Line ~63: `await _apiClient.GetCategoryTreesAsync()`
      - Line ~74: `await _apiClient.GetCategoryTreesAsync()`
    - **Impact**: **MEDIUM** - Category tree resolution operations
    - **Pattern**: Replace with API calls via IApiRequestHandler
    - **Estimated Effort**: 3-4 hours

---

## 🔍 **CATEGORY 6: FACTORY PATTERN**
**Priority**: **HIGH** - Critical for strategy selection

### **Files to Update:**

16. **`EntityDiscoveryStrategyFactory.cs`** - **TODO**
    - **Location**: Line ~84: `await _apiClient.DetectApiVersionAsync()`
    - **Impact**: **HIGH** - API version detection for strategy selection
    - **Pattern**: Replace with health check API call via IApiRequestHandler
    - **Estimated Effort**: 2-3 hours
    - **Dependencies**: Update constructor to inject IApiRequestHandler (already done for product-metafields)

---

## 🔍 **CATEGORY 7: DEPENDENCY INJECTION UPDATES**
**Priority**: **CRITICAL** - Required for all above changes

### **Files to Update:**

17. **All ServiceCollectionExtensions files** - **TODO**
    - **Activities/Extensions/ServiceCollectionExtensions.cs**
    - **Functions/Extensions/ServiceCollectionExtensions.cs**
    - **Pattern**: Update factory registrations to include IApiRequestHandler dependencies
    - **Estimated Effort**: 2-3 hours

---

## 🎯 **Implementation Roadmap**

### **Phase 1: High-Impact Discovery** *(Post Product-Metafields)*
1. **V3EfficientPaginationStrategy** - Affects most entity types
2. **EntityFetchService** - Critical for all fetching operations  
3. **FetchEntityPageActivity** - Core activity used by all entities

### **Phase 2: Critical Activities** 
4. **ValidateMigrationStoresActivity** - Migration startup validation
5. **ResolveCategoryTreeIdsActivity** - Category tree resolution
6. **EntityDiscoveryStrategyFactory** - API version detection

### **Phase 3: Creation Strategy Alignment**
7. **All remaining creation strategies** - Consistency with variants pattern

### **Phase 4: Infrastructure Services**
8. **CategoryTreeResolver** - Infrastructure service alignment

---

## ✅ **Benefits of Migration**

### **Rate Limiting Consistency**
- ✅ All API calls respect dynamic rate limiting
- ✅ Unified rate limit health monitoring
- ✅ Consistent backoff and retry patterns

### **Logging & Monitoring** [[memory:5709531]]
- ✅ Standardized request/response payload logging
- ✅ Consistent error categorization
- ✅ Unified API performance metrics

### **Error Handling** [[memory:5036334]]
- ✅ Proper infrastructure vs application error categorization
- ✅ Consistent continue-on-error policy
- ✅ Structured logging to OpenSearch

### **Performance & Reliability**
- ✅ Unified timeout handling
- ✅ Consistent authentication patterns
- ✅ Predictive rate limiting for all API calls

---

## 📋 **Quality Gates**

### **For Each File Migration:**
- [ ] **Pattern Consistency**: Uses ApiRequest.CreateGet/Post/Put
- [ ] **Rate Limiting**: All calls go through IApiRequestHandler
- [ ] **Error Handling**: Proper exception categorization
- [ ] **Logging**: Request/response payload logging included
- [ ] **Testing**: Unit tests updated to mock IApiRequestHandler
- [ ] **DI Registration**: Constructor dependencies updated
- [ ] **Build Success**: No compilation errors
- [ ] **Backward Compatibility**: No breaking changes

---

## 🚨 **Risk Assessment**

### **High Risk Items:**
- **V3EfficientPaginationStrategy**: Used by most entity types - extensive testing required
- **EntityFetchService**: Core service - any issues affect all entity fetching
- **FetchEntityPageActivity**: Activity used by orchestrators - deterministic behavior critical

### **Mitigation Strategy:**
- **Comprehensive Testing**: Unit tests for each migrated component
- **Gradual Rollout**: Update one file at a time with validation
- **Fallback Planning**: Keep IBigCommerceApiClient temporarily for rollback
- **Performance Monitoring**: Validate no performance degradation

---

**🎯 TOTAL ESTIMATED EFFORT: 40-55 hours across 17 files**

**Next Action**: Complete product-metafields implementation, then tackle high-impact discovery strategies systematically.

This migration is critical for production reliability and consistent rate limiting across the entire BigCommerce migration system.
