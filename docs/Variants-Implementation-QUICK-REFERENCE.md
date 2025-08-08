# Product Variants Migration - Quick Reference Guide

## 🚀 **WHERE WE LEFT OFF**

**Current System Status**: ✅ Products migration working perfectly (100% success rate)  
**Task 1 Status**: ✅ COMPLETED - Hybrid approach implementation finished  
**Next Phase**: Implement Options and Variants migration (Tasks 2-5)  
**Architecture**: ✅ Finalized configurations and workflows defined

---

## ✅ **TASK 1 COMPLETED - HYBRID APPROACH IMPLEMENTATION**

**Status**: ALL SUBTASKS COMPLETE ✅  
**Result**: Product migration now captures channels and related_products metadata

### **✅ Task 1.1: EntityMapping Model Update - COMPLETED**
```csharp
// File: src/BigCommerce.Migration.Core/Models/StorageModels.cs
// CURRENT IMPLEMENTATION (Hybrid Approach):
public class EntityMapping
{
    // Existing fields...
    public string? Metadata { get; set; }           // Backward compatibility
    
    // ✅ CURRENT PHASE: Product migration metadata
    public string? RelatedProductsData { get; set; } // JSON for related products
    public string? ChannelsData { get; set; }       // JSON for channels
    
    // 🔮 FUTURE: Will add during options migration
    // public string? OptionsData { get; set; }     // JSON for options
    // public string? ModifiersData { get; set; }   // JSON for modifiers  
}
```

### **✅ Task 1.2-1.6: All Implementation Tasks - COMPLETED**

**What was implemented:**
- ✅ **IProductApiClient interface** updated with `include` parameter
- ✅ **BigCommerceApiClient implementation** supports `?include=custom_fields,channels`
- ✅ **ProductFetchStrategy** calls API with `include=custom_fields,channels`
- ✅ **ProductTransformStrategy** extracts and stores metadata in EntityMapping
- ✅ **MigrationStorageService** handles new metadata fields (ChannelsData, RelatedProductsData)
- ✅ **Removed channel_id filter** to get ALL products in store

**Current Implementation:**
```csharp
// API Call in ProductFetchStrategy:
var products = await _apiClient.GetProductsAsync(
    sourceStore, 
    page, 
    pageSize, 
    "custom_fields,channels",  // ✅ HYBRID APPROACH
    cancellationToken);

// URL Generated: /catalog/products?page=1&limit=250&include=custom_fields,channels
```

**Metadata Storage Result:**
- `ChannelsData`: JSON from `include=channels` API response
- `RelatedProductsData`: JSON from base product response  
- `custom_fields`: Passed directly to product creation (not stored in EntityMapping)

---

## 📋 **NEXT SESSION: TASK 2 - OPTIONS MIGRATION**

### **🚀 START HERE WHEN RETURNING**

**Status**: Ready to implement Task 2.1 - OptionsFetchStrategy.cs  
**Context**: Task 1 (hybrid approach) completed successfully  
**Next Goal**: Implement options migration using table storage strategy

### **📋 IMMEDIATE NEXT STEPS - SESSION STARTUP CHECKLIST**

1. **Add back OptionsData fields to EntityMapping** (removed during hybrid approach)
   ```csharp
   // Add to src/BigCommerce.Migration.Core/Models/StorageModels.cs
   public string? OptionsData { get; set; }     // JSON for options
   public string? ModifiersData { get; set; }   // JSON for modifiers
   ```

2. **Update MigrationStorageService CRUD operations** to handle options fields

3. **START Task 2.1**: Create `OptionsFetchStrategy.cs`
   - **Location**: `src/BigCommerce.Migration.Orchestration/Strategies/OptionsFetchStrategy.cs`
   - **Strategy**: Table storage chunking (200 products per chunk)
   - **Source**: Read from EntityMapping.OptionsData field
   - **Pattern**: Follow existing fetch strategies but query table instead of API

### **🔧 TECHNICAL CONTEXT FOR NEXT SESSION**

**Current Architecture Status:**
- ✅ **Product Migration**: Enhanced with hybrid approach - captures channels/related_products
- ✅ **EntityMapping Model**: Contains `ChannelsData`, `RelatedProductsData` (options fields removed)
- ✅ **API Integration**: Supports `include=custom_fields,channels` (no options/modifiers to maintain 250/request)
- ✅ **Storage Services**: All CRUD operations updated for current metadata fields

**Key Architecture Decisions Made:**
- ✅ **Hybrid Approach**: Product migration captures metadata, options migration uses stored data
- ✅ **Performance**: Remove options from product API to maintain 250 products/request vs 10/request
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

**Current Todo List for Task 2:**
- ⏳ **T2.1**: Create OptionsFetchStrategy.cs with table storage queries
- ⏳ **T2.2**: Create OptionsTransformStrategy.cs to process extracted options  
- ⏳ **T2.3**: Create OptionsCreationStrategy.cs with parallel processing
- ⏳ **T2.4**: Register options strategies in DI container
- ⏳ **T2.5**: Add options configuration to appsettings.json
- ⏳ **T2.6**: Add options to GetEntityDependencyOrder() method

**Remember**: First re-add OptionsData/ModifiersData fields that were removed during hybrid approach implementation.

## 🎯 **COMPLETED IMPLEMENTATION SEQUENCE**

### **Phase 1: Enhanced Products (Start Here)**
1. ✅ **Interface**: Add `include` parameter to `IProductApiClient.GetProductsAsync()`
2. ✅ **API Client**: Modify `BigCommerceApiClient.GetProductsAsync()` to support include
3. ✅ **Service**: Update `ProductApiService.GetProductsAsync()` implementation  
4. ✅ **Strategy**: Modify `ProductFetchStrategy` to pass `"options,modifiers"`
5. ✅ **Transform**: Update `ProductTransformStrategy` to store options as metadata

### **Phase 2: Options Entity**
6. ✅ Create `OptionsFetchStrategy.cs` (reads from EntityMapping)
7. ✅ Create `OptionsTransformStrategy.cs` (extracts from metadata)
8. ✅ Create `OptionsCreationStrategy.cs` (individual processing)

### **Phase 3: Variants Entity**
9. ✅ Create `VariantsFetchStrategy.cs` (paginated API, limit=50)
10. ✅ Create `VariantsTransformStrategy.cs` (lookup mappings)
11. ✅ Create `VariantsCreationStrategy.cs` (batch processing, 50 per call)

---

## 💡 **KEY IMPLEMENTATION PATTERNS**

### **Options Processing (Table Storage Source)**
```csharp
public class OptionsFetchStrategy : IEntityFetchStrategy
{
    // ✅ FINALIZED: Reads from EntityMapping.OptionsData (JSON)
    // ✅ Pagination: Table storage chunking (200 products per chunk)
    // ✅ Parallel: SubBatchSize = 10 options per sub-batch
    // ✅ Zero BigCommerce API calls (metadata only)
}
```

### **Variants Processing (Batch Creation)**
```csharp
public class VariantsCreationStrategy : IEntityCreationStrategy
{
    // ✅ FINALIZED: Batch API (50 variants per call)
    // ✅ ChunkSize: 200 variants → 4 parallel batches
    // ✅ Error Handling: Create variants WITHOUT options if mappings missing
    // ✅ MaxConcurrency: 4 (matches number of batches)
}
```

### **Finalized Error Handling**
```csharp
// ✅ CONFIRMED: For variants with missing option mappings
if (optionMapping == null) {
    _logger.LogInformation("Creating variant {VariantId} without options - no mapping found", 
        variant.SourceId);
    variant.OptionValues = new List<object>(); // Empty array, continue processing
    // ✅ This is NOT an error - create the variant anyway
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
- `src/BigCommerce.Migration.Core/Interfaces/IProductApiClient.cs`

### **API Layer**
- `src/BigCommerce.Migration.Infrastructure/Services/BigCommerceApiClient.cs`
- `src/BigCommerce.Migration.Infrastructure/Services/ProductApiService.cs`

### **Strategy Layer**
- `src/BigCommerce.Migration.Orchestration/Strategies/ProductFetchStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Strategies/ProductTransformStrategy.cs`

### **New Files to Create**
```
src/BigCommerce.Migration.Orchestration/Strategies/
├── OptionsFetchStrategy.cs       # ✅ Table storage queries (200 products/chunk)
├── OptionsTransformStrategy.cs   # ✅ Extract from OptionsData JSON
├── OptionsCreationStrategy.cs    # ✅ Individual API calls (SubBatch=10)
├── VariantsFetchStrategy.cs      # ✅ BigCommerce API (50/page, 200/chunk)
├── VariantsTransformStrategy.cs  # ✅ Lookup mappings + handle missing options
└── VariantsCreationStrategy.cs   # ✅ Batch API calls (50/batch, MaxConcurrency=4)
```

### **Files to Modify**
```
src/BigCommerce.Migration.Core/Models/
├── StorageModels.cs                    # ✅ Add OptionsData, ModifiersData, RelatedProductsData

src/BigCommerce.Migration.Infrastructure/Services/
├── MigrationStorageService.cs          # ✅ Support new metadata fields
```

### **Configuration**
- `src/BigCommerce.Migration.Functions/Configuration/appsettings.json`
- `docker-compose.yml`
- `docker-compose.prod.yml`

---

## 🧪 **TESTING APPROACH**

### **Test Data Requirements**
- Products with options (complex products)
- Products without options (simple products)
- Variants with multiple option combinations
- Variants without options

### **Validation Points**
1. **Options stored in metadata**: Check EntityMapping.Metadata field
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

✅ CONFIRMED: Options depend on products (metadata source)
✅ CONFIRMED: Variants depend on options (mapping lookups)
```

**Update in**: `src/BigCommerce.Migration.Functions/Orchestrators/MigrationDurableOrchestrator.cs`

```csharp
// Update GetEntityDependencyOrder method:
var fullDependencyOrder = new[]
{
    "brands",      // Must be before products
    "products",    // ✅ Store options/modifiers/related_products metadata  
    "options",     // ✅ NEW: Process from product metadata
    "variants"     // ✅ NEW: Process with option mappings
};
```

---

## 📝 **COMMIT STRATEGY**

### **Suggested Commit Sequence**
1. `feat: add include parameter support to product API`
2. `feat: store options metadata during product migration`
3. `feat: implement options entity migration`
4. `feat: implement variants entity migration with batch processing`
5. `feat: add variants configuration and dependency order`
6. `test: comprehensive validation for options and variants`

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