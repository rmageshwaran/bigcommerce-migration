# Product Variants Migration - Quick Reference Guide

## 🚀 **WHERE WE LEFT OFF**

**Current System Status**: ✅ Products migration working perfectly (100% success rate)  
**Task 1 Status**: 🔴 **NOT STARTED** - Hybrid approach implementation needed  
**Next Phase**: Implement Options and Variants migration (Tasks 1-5)  
**Architecture**: ✅ Finalized configurations and workflows defined

---

## 🔴 **TASK 1 - HYBRID APPROACH IMPLEMENTATION - NOT STARTED**

**Status**: 🔴 **NOT IMPLEMENTED**  
**Result**: Product migration currently captures channels and related_products metadata only

### **🔴 Task 1.1: EntityMapping Model Update - NOT IMPLEMENTED**
```csharp
// File: src/BigCommerce.Migration.Core/Models/StorageModels.cs
// CURRENT IMPLEMENTATION (Partial):
public class EntityMapping
{
    // Existing fields...
    public string? Metadata { get; set; }           // Backward compatibility
    
    // ✅ CURRENT PHASE: Product migration metadata (PARTIAL)
    public string? RelatedProductsData { get; set; } // JSON for related products
    public string? ChannelsData { get; set; }       // JSON for channels
    
    // 🔴 MISSING: Options and modifiers fields
    // public string? OptionsData { get; set; }     // JSON for options
    // public string? ModifiersData { get; set; }   // JSON for modifiers  
}
```

### **🔴 Task 1.2-1.6: All Implementation Tasks - NOT IMPLEMENTED**

**What needs to be implemented:**
- 🔴 **IProductApiClient interface** already supports `include` parameter ✅
- 🔴 **BigCommerceApiClient implementation** already supports `?include=options,modifiers` ✅
- 🔴 **ProductFetchStrategy** currently uses `"custom_fields,channels"` - needs to include options/modifiers
- 🔴 **ProductTransformStrategy** needs to extract and store options/modifiers metadata
- 🔴 **MigrationStorageService** needs to handle new metadata fields (OptionsData, ModifiersData)

**Current Implementation:**
```csharp
// API Call in ProductFetchStrategy:
var products = await _apiClient.GetProductsAsync(
    sourceStore, 
    page, 
    pageSize, 
    "custom_fields,channels",  // 🔴 NEEDS: "options,modifiers,custom_fields,channels"
    cancellationToken);

// URL Generated: /catalog/products?page=1&limit=250&include=custom_fields,channels
```

**Current Metadata Storage Result:**
- `ChannelsData`: JSON from `include=channels` API response ✅
- `RelatedProductsData`: JSON from base product response ✅
- `OptionsData`: **MISSING** - not extracted or stored
- `ModifiersData`: **MISSING** - not extracted or stored

---

## 📋 **NEXT SESSION: TASK 1 - HYBRID APPROACH IMPLEMENTATION**

### **🚀 START HERE WHEN RETURNING**

**Status**: Ready to implement Task 1.1 - Add OptionsData/ModifiersData fields  
**Context**: Product migration working, API support exists, need metadata capture  
**Next Goal**: Implement hybrid approach to capture options/modifiers during product migration

### **📋 IMMEDIATE NEXT STEPS - SESSION STARTUP CHECKLIST**

1. **Add OptionsData and ModifiersData fields to EntityMapping** (Task 1.1)
   ```csharp
   // Add to src/BigCommerce.Migration.Core/Models/StorageModels.cs
   public string? OptionsData { get; set; }     // JSON for options
   public string? ModifiersData { get; set; }   // JSON for modifiers
   ```

2. **Update MigrationStorageService CRUD operations** to handle options fields (Task 1.2)

3. **Update ProductFetchStrategy** to include options/modifiers (Task 1.3)
   ```csharp
   // Change from "custom_fields,channels" to:
   "options,modifiers,custom_fields,channels"
   ```

4. **Update ProductTransformStrategy** to extract and store options metadata (Task 1.4)

### **🔧 TECHNICAL CONTEXT FOR NEXT SESSION**

**Current Architecture Status:**
- ✅ **Product Migration**: Working with channels/related_products metadata
- 🔴 **EntityMapping Model**: Missing `OptionsData`, `ModifiersData` fields
- ✅ **API Integration**: Supports `include=options,modifiers,custom_fields,channels`
- 🔴 **Storage Services**: Need updates for new metadata fields

**Key Architecture Decisions Made:**
- ✅ **Hybrid Approach**: Product migration will capture metadata, options migration will use stored data
- ✅ **Performance**: Include options in product API to capture data efficiently
- ✅ **No channel_id filter**: Get ALL products in store, handle channels separately
- ✅ **Table Storage Source**: Options will read from EntityMapping table, not BigCommerce API

**Configuration Values Confirmed:**
```javascript
// Options (for Task 2):
"chunkSize": 200,     // Products to process per chunk
"fetchBatchSize": 1,  // Individual product options API call  
"pageSize": 1,        // 1 product per response
"subBatchSize": 10,   // Options per sub-batch for parallel creation
"maxConcurrency": 10  // 10 concurrent API calls
```

**Current Todo List for Task 1:**
- 🔴 **T1.1**: Add OptionsData/ModifiersData fields to EntityMapping model
- 🔴 **T1.2**: Update MigrationStorageService to handle new fields
- 🔴 **T1.3**: Update ProductFetchStrategy to include options/modifiers
- 🔴 **T1.4**: Update ProductTransformStrategy to extract and store metadata

**Remember**: Start with the hybrid approach foundation - add metadata fields and capture options during product migration.

## 🎯 **COMPLETED IMPLEMENTATION SEQUENCE**

### **Phase 1: Enhanced Products (Start Here)**
1. 🔴 **Interface**: Add `include` parameter to `IProductApiClient.GetProductsAsync()` ✅ **ALREADY DONE**
2. 🔴 **API Client**: Modify `BigCommerceApiClient.GetProductsAsync()` to support include ✅ **ALREADY DONE**
3. 🔴 **Service**: Update `ProductApiService.GetProductsAsync()` implementation ✅ **ALREADY DONE**
4. 🔴 **Strategy**: Modify `ProductFetchStrategy` to pass `"options,modifiers"` ❌ **NOT DONE**
5. 🔴 **Transform**: Update `ProductTransformStrategy` to store options as metadata ❌ **NOT DONE**

### **Phase 2: Options Entity**
6. 🔴 Create `OptionsFetchStrategy.cs` (reads from EntityMapping) ❌ **NOT DONE**
7. 🔴 Create `OptionsTransformStrategy.cs` (extracts from metadata) ❌ **NOT DONE**
8. 🔴 Create `OptionsCreationStrategy.cs` (individual processing) ❌ **NOT DONE**

### **Phase 3: Variants Entity**
9. 🔴 Create `VariantsFetchStrategy.cs` (paginated API, limit=50) ✅ **BASIC STUB EXISTS**
10. 🔴 Create `VariantsTransformStrategy.cs` (lookup mappings) ✅ **BASIC STUB EXISTS**
11. 🔴 Create `VariantsCreationStrategy.cs` (batch processing, 50 per call) ✅ **BASIC STUB EXISTS**

---

## 💡 **KEY IMPLEMENTATION PATTERNS**

### **Options Processing (Table Storage Source)**
```csharp
public class OptionsFetchStrategy : IEntityFetchStrategy
{
    // 🔴 NOT IMPLEMENTED: Reads from EntityMapping.OptionsData (JSON)
    // 🔴 NOT IMPLEMENTED: Pagination: Table storage chunking (200 products per chunk)
    // 🔴 NOT IMPLEMENTED: Parallel: SubBatchSize = 10 options per sub-batch
    // 🔴 NOT IMPLEMENTED: Zero BigCommerce API calls (metadata only)
}
```

### **Variants Processing (Batch Creation)**
```csharp
public class VariantsCreationStrategy : IEntityCreationStrategy
{
    // ✅ BASIC STUB EXISTS: Entity type and interface implemented
    // 🔴 NOT IMPLEMENTED: Batch API (50 variants per call)
    // 🔴 NOT IMPLEMENTED: ChunkSize: 200 variants → 4 parallel batches
    // 🔴 NOT IMPLEMENTED: Error Handling: Create variants WITHOUT options if mappings missing
    // 🔴 NOT IMPLEMENTED: MaxConcurrency: 4 (matches number of batches)
}
```

### **Finalized Error Handling**
```csharp
// 🔴 NOT IMPLEMENTED: For variants with missing option mappings
if (optionMapping == null) {
    _logger.LogInformation("Creating variant {VariantId} without options - no mapping found", 
        variant.SourceId);
    variant.OptionValues = new List<object>(); // Empty array, continue processing
    // This is NOT an error - create the variant anyway
}
```

---

## 🔧 **FINALIZED CONFIGURATION VALUES**

### **Options Configuration**
```json
{
  "ParallelProcessing": {
    "subBatchConfigurations": {
      "options": {
        "chunkSize": 200,                   // ✅ Products to query per chunk
        "fetchBatchSize": 200,              // ✅ Table storage query size
        "pageSize": 200,                    // Same as chunkSize  
        "subBatchSize": 10,                 // ✅ Options per sub-batch (CONFIRMED)
        "maxConcurrency": 10,               // Parallel sub-batches
        "subBatchDelayMs": 50,              // Rate limiting delay
        "processSubBatchesSequentially": false
      }
    }
  }
}
```

### **Variants Configuration**
```json
{
  "ParallelProcessing": {
    "subBatchConfigurations": {
      "variants": {
        "chunkSize": 200,                   // ✅ Variants per chunk (CONFIRMED)
        "fetchBatchSize": 50,               // BigCommerce API page limit
        "pageSize": 50,                     // Same as fetchBatchSize
        "subBatchSize": 50,                 // Variants per batch API call
        "maxConcurrency": 4,                // ✅ Match batches (CONFIRMED)
        "subBatchDelayMs": 100,             // Rate limiting delay
        "processSubBatchesSequentially": false
      }
    }
  }
}
```

### **Docker Environment Variables**
```yaml
# Options Configuration
environment:
  - ParallelProcessing__subBatchConfigurations__options__chunkSize=200
  - ParallelProcessing__subBatchConfigurations__options__fetchBatchSize=200
  - ParallelProcessing__subBatchConfigurations__options__subBatchSize=10
  - ParallelProcessing__subBatchConfigurations__options__maxConcurrency=10

# Variants Configuration  
  - ParallelProcessing__subBatchConfigurations__variants__chunkSize=200
  - ParallelProcessing__subBatchConfigurations__variants__fetchBatchSize=50
  - ParallelProcessing__subBatchConfigurations__variants__subBatchSize=50
  - ParallelProcessing__subBatchConfigurations__variants__maxConcurrency=4
```

---

## 📁 **FILE LOCATIONS**

### **Core Interfaces**
- `src/BigCommerce.Migration.Core/Interfaces/IProductApiClient.cs` ✅ **SUPPORTS INCLUDE**

### **API Layer**
- `src/BigCommerce.Migration.Infrastructure/Services/BigCommerceApiClient.cs` ✅ **SUPPORTS INCLUDE**
- `src/BigCommerce.Migration.Infrastructure/Services/ProductApiService.cs` ✅ **SUPPORTS INCLUDE**

### **Strategy Layer**
- `src/BigCommerce.Migration.Orchestration/Strategies/ProductFetchStrategy.cs` 🔴 **NEEDS UPDATE**
- `src/BigCommerce.Migration.Orchestration/Strategies/ProductTransformStrategy.cs` 🔴 **NEEDS UPDATE**

### **New Files to Create**
```
src/BigCommerce.Migration.Orchestration/Strategies/
├── OptionsFetchStrategy.cs       // 🔴 NOT CREATED - Table storage queries (200 products/chunk)
├── OptionsTransformStrategy.cs   // 🔴 NOT CREATED - Extract from OptionsData JSON
├── OptionsCreationStrategy.cs    // 🔴 NOT CREATED - Individual API calls (SubBatch=10)
├── VariantsFetchStrategy.cs      // ✅ EXISTS - Basic stub, needs enhancement
├── VariantsTransformStrategy.cs  // ✅ EXISTS - Basic stub, needs enhancement
└── VariantsCreationStrategy.cs   // ✅ EXISTS - Basic stub, needs enhancement
```

### **Files to Modify**
```
src/BigCommerce.Migration.Core/Models/
├── StorageModels.cs                    // 🔴 NEEDS: Add OptionsData, ModifiersData

src/BigCommerce.Migration.Infrastructure/Services/
├── MigrationStorageService.cs          // 🔴 NEEDS: Support new metadata fields
```

### **Configuration**
- `src/BigCommerce.Migration.Functions/Configuration/appsettings.json` 🔴 **NEEDS OPTIONS/VARIANTS CONFIG**
- `docker-compose.yml` 🔴 **NEEDS ENVIRONMENT VARIABLES**
- `docker-compose.prod.yml` 🔴 **NEEDS ENVIRONMENT VARIABLES**

---

## 🧪 **TESTING APPROACH**

### **Test Data Requirements**
- Products with options (complex products)
- Products without options (simple products)
- Variants with multiple option combinations
- Variants without options

### **Validation Points**
1. **Options stored in metadata**: Check EntityMapping.OptionsData field
2. **Options created correctly**: Verify option and option_value mappings
3. **Variants processed gracefully**: Test missing mapping scenarios
4. **Batch efficiency**: Confirm 50 variants per API call

---

## ⚡ **FINALIZED PERFORMANCE METRICS**

### **Options Processing Performance**
- **✅ Zero BigCommerce API calls** (table storage only)
- **✅ Parallel efficiency**: 10 concurrent sub-batches × 10 options = 100 parallel API calls per round
- **✅ Memory optimization**: 200 products per chunk (manageable size)
- **✅ Table storage queries**: Optimized with proper pagination

### **Variants Processing Performance**  
- **✅ Batch efficiency**: 50 variants per API call (BigCommerce maximum)
- **✅ Parallel batches**: 4 concurrent batch API calls (200 ÷ 50)
- **✅ Error resilience**: Create variants without options (no batch failures)
- **✅ Memory usage**: 200 variants per chunk

### **Monitoring Points**
- **Options**: API call count should remain zero for options processing
- **Variants**: Batch creation efficiency (target: 50 variants per call)
- **Error handling**: Variants created without options (should not be errors)
- **Parallel efficiency**: Sub-batch and batch concurrency utilization

---

## 🔄 **FINALIZED DEPENDENCY ORDER**

```
Current: brands → products
Target:  brands → products → options → variants

🔴 NOT IMPLEMENTED: Options depend on products (metadata source)
🔴 NOT IMPLEMENTED: Variants depend on options (mapping lookups)
```

**Update in**: `src/BigCommerce.Migration.Functions/Orchestrators/MigrationDurableOrchestrator.cs`

```csharp
// Current implementation (lines 446-450):
var fullDependencyOrder = new[]
{
    "brands",      // Must be before products
    "products"     // Focus on product migration performance and timeout testing
};

// Target implementation:
var fullDependencyOrder = new[]
{
    "brands",      // Must be before products
    "products",    // ✅ Store options/modifiers/related_products metadata  
    "options",     // 🔴 NEW: Process from product metadata
    "variants"     // 🔴 NEW: Process with option mappings
};
```

---

## 📝 **COMMIT STRATEGY**

### **Suggested Commit Sequence**
1. `feat: add OptionsData and ModifiersData fields to EntityMapping model`
2. `feat: update ProductFetchStrategy to include options and modifiers`
3. `feat: update ProductTransformStrategy to store options metadata`
4. `feat: implement options entity migration strategies`
5. `feat: implement variants entity migration with batch processing`
6. `feat: add variants configuration and dependency order`
7. `test: comprehensive validation for options and variants`

---

## 🚨 **WATCH OUT FOR**

### **Common Pitfalls**
- **Chunking misalignment**: Ensure `chunkSize = fetchBatchSize` for variants
- **Metadata size limits**: Monitor options JSON size (64KB Azure limit)
- **Error cascading**: Don't fail entire batches for missing option mappings
- **API rate limits**: Respect BigCommerce limits with proper delays

### **Success Indicators**
- Products migration continues to work (no regression)
- Options created without additional API calls
- Variants created efficiently in batches
- High success rate maintained (99%+ target)

---

*Use this guide to quickly resume implementation from where we left off.*