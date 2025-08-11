# Enhanced Product Migration - Detailed Task Breakdown

## 🎯 **SYSTEM OVERVIEW**

This document provides comprehensive task breakdown for implementing the **6-Phase Enhanced Product Migration Strategy** with **on-the-fly entity creation** for optimal performance and simplified architecture.

### **🚀 FINAL ACCEPTED STRATEGY**

**Simplified Migration Flow:**
```
brands → products → options_modifiers_images_reviews → variants → channels → related_products → metafields
   ↓         ↓                      ↓                      ↓          ↓          ↓               ↓
mapping   mapping            ON-THE-FLY CREATION        mapping    update     update          batch
```

**Key Performance & Architecture Decisions:**
- ✅ **Phase 2**: Single comprehensive entity migration (10/page but creates ALL entities immediately)
- ✅ **No Blob Storage**: All entities created on-the-fly, no intermediate storage
- ✅ **JSON Mappings**: Option mappings stored in EntityMapping for variants
- ✅ **Real-time Updates**: SignalR events for every batch (10 products)
- ✅ **Count Tracking**: Individual entity counts in EntityMapping

---

## 🚨 **PRIORITY TASKS - INFRASTRUCTURE FIXES**

**These tasks MUST be completed FIRST before any Enhanced Product Migration implementation.**

### **P0-T1: Fix Existing SignalR Integration** 🔴 **CRITICAL**
**Priority**: HIGHEST | **Effort**: 6 hours | **Dependencies**: None

**Issue**: Brand and product migrations have SignalR code but real-time updates aren't working
**Root Cause**: ProcessEntityChunkActivity uses basic IProgressEventPublisher instead of ISignalREventFactory

**Sub-tasks:**
1. **P0-T1.1**: Investigate SignalR integration flow for brands/products
2. **P0-T1.2**: Add SignalR events to ProcessEntityChunkActivity for real-time updates  
3. **P0-T1.3**: Fix small dataset workflows that bypass parallel processing
4. **P0-T1.4**: Verify SignalR events reach dashboard for brands/products
5. **P0-T1.5**: Test batch-level and cumulative progress updates

**Acceptance Criteria:**
- ✅ Real-time progress updates working for brand migration
- ✅ Real-time progress updates working for product migration  
- ✅ Batch-level progress visible in dashboard
- ✅ Small datasets (≤threshold) also show progress updates

### **P0-T2: Fix Existing API Error Logging** ✅ **COMPLETED**  
**Priority**: HIGHEST | **Effort**: 4 hours | **Dependencies**: None

**Issue**: API error logging infrastructure exists but exceptions aren't properly logged to OpenSearch
**Root Cause**: Missing integration between API services and EntityErrorHandlingService

**Sub-tasks:**
1. **P0-T2.1**: ✅ Enhanced API error logging in all fetch strategies
2. **P0-T2.2**: ✅ Added comprehensive error context (store ID, entity IDs, error type)
3. **P0-T2.3**: ✅ Implemented request/response payload logging patterns
4. **P0-T2.4**: ✅ Added proper error categorization (Category="Error")
5. **P0-T2.5**: ✅ Enhanced error logging in V3EfficientPaginationStrategy

**Acceptance Criteria:**
- ✅ API errors logged with comprehensive context (store, entity IDs, error type)
- ✅ Enhanced error logging in VariantFetchStrategy, ImageFetchStrategy, ModifierFetchStrategy
- ✅ Added exception handling to CategoryFetchStrategy
- ✅ Fixed V3EfficientPaginationStrategy error logging
- ✅ Consistent error message format across all fetch strategies

**Files Modified:**
- `src/BigCommerce.Migration.Orchestration/Strategies/VariantFetchStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Strategies/ImageFetchStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Strategies/ModifierFetchStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Strategies/CategoryFetchStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Strategies/V3EfficientPaginationStrategy.cs`

---

## 🏗️ **PHASE 1: ENHANCED PRODUCTS MIGRATION**

### **P1-T1: Update Include Parameters** ⭐ ✅ **COMPLETED**
**Priority**: Critical | **Effort**: 2 hours | **Dependencies**: None

**Sub-tasks:**
1. **P1-T1.1**: ✅ Update include parameters for direct pagination strategy
   - ✅ Enhanced `V3EfficientPaginationStrategy` to support include parameters
   - ✅ Updated `BigCommercePaginationRequest` model with Include property
   - ✅ Enhanced `BigCommerceApiClient.BuildEntityUrl` to append include parameter
   - ✅ Modified `EntityFetchService.FetchEntitiesWithDirectPaginationAsync` to use includes

2. **P1-T1.2**: ✅ Configure include parameters in appsettings.json
   - ✅ Products: "bulk_pricing_rules,custom_fields,channels,videos"
   - ✅ Enhanced-products: "options,modifiers,images,reviews"

**Acceptance Criteria:**
- ✅ Include parameters configured for both products and enhanced-products
- ✅ Direct pagination strategy supports include functionality
- ✅ No breaking changes to existing product migration

**Files Modified:**
- `src/BigCommerce.Migration.Core/Models/BigCommercePaginationModels.cs`
- `src/BigCommerce.Migration.Infrastructure/Services/BigCommerceApiClient.cs`
- `src/BigCommerce.Migration.Orchestration/Strategies/V3EfficientPaginationStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Services/EntityFetchService.cs`
- `src/BigCommerce.Migration.Functions/appsettings.json`

**NOTE**: ProductFetchStrategy.cs was removed as it was unused - current implementation uses direct pagination.

---

### **P1-T2: Enhance Product Creation Payload** ⭐ ✅ **CORRECTED**
**Priority**: Critical | **Effort**: 1 hour | **Dependencies**: P1-T1

**📌 CLARIFICATION**: Enhanced data should be included in **BigCommerce API creation payload**, not stored in EntityMapping table.

**Sub-tasks:**
1. **P1-T2.1**: ✅ Enhanced product data is fetched via include parameters (completed in P1-T1)
   - ✅ BulkPricingRules - Enhanced include parameter fetches this data
   - ✅ Videos - Enhanced include parameter fetches this data  
   - ✅ CustomFields - Enhanced include parameter fetches this data
   - ✅ RelatedProducts - Already supported via include parameter
   - ✅ Channels - Already supported via include parameter

2. **P1-T2.2**: ✅ Enhanced data flows to product creation payload (no storage changes needed)
   - ✅ API payload includes all enhanced data from include parameters
   - ✅ EntityMapping remains unchanged - only tracks migration relationships

**Acceptance Criteria:**
- ✅ New fields added without breaking existing data
- ✅ Storage service handles new fields correctly
- ✅ Database migration (if needed) completed successfully

**Files to Modify:**
- `src/BigCommerce.Migration.Core/Models/StorageModels.cs`
- `src/BigCommerce.Migration.Infrastructure/Services/MigrationStorageService.cs`

---

### **P1-T3: Enhance Product Transform Strategy** ⭐ ✅ **ALREADY COMPLETE**
**Priority**: Critical | **Effort**: 3 hours | **Dependencies**: P1-T2

**Sub-tasks:**
1. **P1-T3.1**: Update `ExtractAndStoreMetadataAsync` method
   ```csharp
   // Add extraction for new include types:
   string? bulkPricingRulesData = ExtractDataAsJson(entity, "bulk_pricing_rules");
   string? videosData = ExtractDataAsJson(entity, "videos"); 
   string? customFieldsData = ExtractDataAsJson(entity, "custom_fields");
   
   // Store reviews in blob storage for separate migration
   await StoreReviewsInBlobStorageAsync(entity, migrationId, sourceId);
   
   // Store in EntityMapping
   existingMapping.BulkPricingRulesData = bulkPricingRulesData;
   existingMapping.VideosData = videosData;
   existingMapping.CustomFieldsData = customFieldsData;
   ```

2. **P1-T3.2**: Enhance create payload integration
   ```csharp
   // Include directly in product creation payload:
   if (entity.TryGetValue("bulk_pricing_rules", out var bulkPricingRulesValue))
   {
       transformed["bulk_pricing_rules"] = bulkPricingRulesValue;
   }
   
   if (entity.TryGetValue("videos", out var videosValue))
   {
       transformed["videos"] = videosValue;
   }
   
   if (entity.TryGetValue("custom_fields", out var customFieldsValue))
   {
       transformed["custom_fields"] = customFieldsValue;
   }
   
   // NOTE: Reviews are NOT included in create payload - stored in blob storage for separate migration
   ```

3. **P1-T3.3**: Update `TransformBulkPricingRules`, `TransformVideoFields`, `TransformCustomFields` methods
   - Integrate data directly into create payload instead of storing for later
   - Maintain backward compatibility

**Acceptance Criteria:**
- ✅ New metadata extracted and stored correctly
- ✅ bulk_pricing_rules, videos, custom_fields included in create payload
- ✅ No regression in existing transform logic
- ✅ Proper error handling for missing data

**Files to Modify:**
- `src/BigCommerce.Migration.Orchestration/Strategies/ProductTransformStrategy.cs`

---

### **P1-T4: Update Configuration** 
**Priority**: Medium | **Effort**: 1 hour | **Dependencies**: None

**Sub-tasks:**
1. **P1-T4.1**: Update configuration files
   ```json
   {
     "ParallelProcessing": {
       "subBatchConfigurations": {
         "products": {
           "chunkSize": 250,
           "fetchBatchSize": 250,
           "pageSize": 250,
           "subBatchSize": 1,
           "maxConcurrency": 5,
           "include": "bulk_pricing_rules,custom_fields,channels,videos,reviews"
         }
       }
     }
   }
   ```

**Files to Modify:**
- `src/BigCommerce.Migration.Functions/appsettings.json`
- `docker-compose.yml` (environment variables)

---

## 🧠 **PHASE 2: COMPREHENSIVE ENTITY MIGRATION (SEAMLESS INTEGRATION)**

### **P2-T1: Configuration-Driven Enhanced Products Integration** ⭐
**Priority**: Critical | **Effort**: 8 hours | **Dependencies**: P0-T1, P0-T2 (MUST complete infrastructure fixes first)

**Overview**: Seamlessly integrate enhanced products processing with **dual-tier progress aggregation** using existing orchestration infrastructure. No orchestrator changes needed - only configuration and strategy updates.

**Integration Strategy:**
```
EntityMigrationOrchestrator → ProcessEntityChunkActivity → ComprehensiveEntityMigrationPipeline
     ↓ (existing)                   ↓ (enhanced)              ↓ (new)
Configuration-driven         Detects enhanced-products    Parallel Sub-Entity Processing
   pageSize: 10             routes to comprehensive         [Options, Modifiers, Images, Reviews]
```

**Sub-tasks:**
1. **P2-T1.1**: Add Enhanced Products Configuration
   ```json
   // appsettings.json - ParallelProcessing section
   "enhanced-products": {
     "entityType": "enhanced-products",
     "pageSize": 10,                    // API LIMIT: Max 10 with includes
     "chunkSize": 10,
     "fetchBatchSize": 10,
     "subBatchSize": 2,
     "maxConcurrency": 5,
     "enableSubBatching": true,
     "enableParallelSubEntities": true  // NEW FLAG
   }
   ```

2. **P2-T1.2**: Create EnhancedProductFetchStrategy
   ```csharp
   public class EnhancedProductFetchStrategy : IEntityFetchStrategy
   {
       public async Task<List<Dictionary<string, object>>> FetchEntitiesAsync(...)
       {
           // Enhanced includes with API limit
           return await _apiClient.GetProductsAsync(
               sourceStore, page, 10,  // API LIMIT: Max 10
               "custom_fields,channels,bulk_pricing_rules,videos,options,modifiers",
               cancellationToken);
       }
   }
   ```

3. **P2-T1.3**: Implement ComprehensiveEntityMigrationPipeline
   ```csharp
   public class ComprehensiveEntityMigrationPipeline : IComprehensiveEntityMigrationPipeline
   {
       public async Task<BatchProcessingResult> ProcessComprehensiveEntitiesAsync(
           List<Dictionary<string, object>> products,
           BatchProcessingRequest request,
           CancellationToken cancellationToken)
       {
           // Initialize Pipeline Progress Aggregator (Tier 1)
           using var pipelineAggregator = new PipelineProgressAggregator(
               request.MigrationId, channelConfigs, _signalREventFactory, 
               _universalAggregator, _logger);
           
           foreach (var product in products)
           {
               // Create main product
               await CreateMainProductAsync(product, request, cancellationToken);
               
               // Parallel sub-entity processing
               var tasks = new List<Task>
               {
                   ProcessOptionsWithProgressAsync(product, request, pipelineAggregator, cancellationToken),
                   ProcessModifiersWithProgressAsync(product, request, pipelineAggregator, cancellationToken),
                   ProcessImagesWithProgressAsync(product, request, pipelineAggregator, cancellationToken),
                   ProcessReviewsWithProgressAsync(product, request, pipelineAggregator, cancellationToken)
               };
               
               await Task.WhenAll(tasks);
           }
       }
   }
   ```

4. **P2-T1.4**: Enhance ProcessEntityChunkActivity Integration
   ```csharp
   public class ProcessEntityChunkActivity  // EXISTING CLASS
   {
       private readonly IComprehensiveEntityMigrationPipeline _comprehensivePipeline;
       private readonly IUniversalMigrationProgressAggregator _universalAggregator;
       
       public async Task<BatchProcessingResult> ProcessEntityChunkAsync([ActivityTrigger] ProcessEntityChunkRequest request)
       {
           // Existing validation and setup...
           
           if (request.EntityType == "enhanced-products")
           {
               // Use comprehensive pipeline for parallel sub-entity processing
               var result = await _comprehensivePipeline.ProcessComprehensiveEntitiesAsync(
                   fetchedEntities, batchRequest, cancellationToken);
               
               // Update Universal Aggregator (Tier 2)
               await _universalAggregator.UpdatePrimaryEntityProgressAsync(
                   request.MigrationId, "enhanced-products", 
                   new PrimaryEntityProgress { /* result data */ });
                   
               return result;
           }
           else
           {
               // Standard processing for brands, products
               return await ProcessStandardEntitiesAsync(fetchedEntities, batchRequest, cancellationToken);
           }
       }
   }
   ```

5. **P2-T1.5**: Implement Dual-Tier Progress Aggregation
   
   **🏗️ Tier 1: Pipeline Progress Aggregator (Within Chunks)**
   - ✅ **Real-time channel updates**: Options, Modifiers, Images, Reviews progress
   - ✅ **Timer-based broadcasting**: 250ms intervals for optimal UX
   - ✅ **Bridge to Universal Aggregator**: Automatic integration with Tier 2
   
   **🏗️ Tier 2: Universal Migration Progress Aggregator (Ecosystem-wide)**
   - ✅ **Primary entity tracking**: brands, products, enhanced-products
   - ✅ **Comprehensive entity tracking**: options, modifiers, images, reviews
   - ✅ **Unified dashboard events**: Complete migration visibility
   
   **📊 Dashboard Integration:**
   ```typescript
   // Dashboard receives both event types:
   
   // Tier 1: Individual comprehensive entity progress
   interface ComprehensiveEntityProgressEvent {
     migrationId: string;
     parentEntityType: "products";
     entityType: "options" | "modifiers" | "images" | "reviews";
     processedCount: number;
     throughputPerSecond: number;
   }
   
   // Tier 2: Universal migration progress
   interface UniversalMigrationProgressEvent {
     migrationId: string;
     primaryEntityProgress: { brands: Progress; products: Progress; "enhanced-products": Progress; };
     comprehensiveEntityProgress: { products: { options: Progress; modifiers: Progress; } };
   }
   ```
   
   **🔄 Integration Benefits:**
   - ✅ **Zero orchestrator changes**: Uses existing EntityMigrationOrchestrator flow
   - ✅ **Configuration-driven**: Simply add enhanced-products config
   - ✅ **Timeout safety**: Leverages existing ProcessEntityChunkActivity (10 products max)
   - ✅ **Real-time updates**: Dual-tier progress for granular + overview visibility
   - Parallel entity creation within each batch
   - Progress persistence after each batch completion
   - Resumable from last completed batch on timeout/failure

3. **P2-T1.3**: Implement Real-time SignalR Integration  
   - Follow fixed SignalR pattern from P0-T1
   - Publish progress after each batch (10 products)
   - Send entity statistics: Options: 100/100, Modifiers: 90/95, etc.
   - Include batch progress and overall progress

4. **P2-T1.4**: Implement Individual Entity Error Handling
   - Follow fixed error logging pattern from P0-T2  
   - Category="Error" for API errors, Category="Application" for system errors
   - Log request/response payloads for each failed entity
   - Continue processing on individual entity failures

5. **P2-T1.5**: Integrate Live Cancellation Support ⚠️ **MANDATORY**
   - **4-Level Cancellation**: Migration → EntityType → Batch → Store (Memory 5019419)
   - Cancellation checks in activities using `ILiveCancellationManager.IsCancelledAsync()`
   - Orchestrator-level checks using `CheckLiveCancellationActivity`
   - Graceful cleanup and status updates on cancellation
   - Real-time SignalR updates for cancellation events

6. **P2-T1.6**: Add workflow integration with timeout safety
   
   **🔄 Durable Functions Timeout Management:**
   ```csharp
   public class ComprehensiveEntityMigrationOrchestrator : TaskOrchestrator
   {
       public async Task<ComprehensiveEntityMigrationResult> RunOrchestrator(
           [OrchestrationTrigger] IDurableOrchestrationContext context)
       {
           // Smart count source (no API overhead)
           var totalProducts = await context.CallActivityAsync<int>(
               nameof(GetEntityCountActivity), 
               new GetEntityCountRequest(migrationId, "products"));
           
           // Timeout-safe batch processing
           var batchSize = 10; // Ensures each batch completes within Function timeout
           var batchRequests = CreateBatchRequests(totalProducts, batchSize);
           
           // Parallel execution with timeout resilience
           var results = await context.CallSubOrchestratorAsync<List<BatchResult>>(
               nameof(ProcessEntityChunkActivity), 
               new ParallelBatchRequest(batchRequests));
               
           return AggregateResults(results);
       }
   }
   ```
   
   **⏱️ Function App Timeout Considerations:**
   - **Batch Size**: 10 products max (estimated 2-3 minutes per batch)
   - **Activity Timeout**: Individual activities complete within 5-minute limit
   - **Orchestrator Pattern**: Uses sub-orchestrators for timeout safety
   - **Progress Persistence**: Each batch completion persisted immediately
   - **Resumable Design**: Can continue from last successful batch
   
   **🔗 Integration Points:**
   - Update `MigrationDurableOrchestrator.cs` to call new phase
   - Phase name: `options_modifiers_images_reviews`
   - Dependency: Execute after products migration completion
   - Error handling: Individual entity failures don't stop migration

**Acceptance Criteria:**
- ✅ Single phase creates options, modifiers, images, reviews on-the-fly
- ✅ Real-time SignalR updates every 10 products  
- ✅ Individual entity counts displayed: Options: X/Y, Modifiers: A/B, etc. (from EntityProgressEntry)
- ✅ Option mappings stored in EntityMapping JSON for variants
- ✅ API errors logged with full request/response context
- ✅ No blob storage dependencies
- ✅ EntityMapping table kept clean (no count duplication)

**Note**: No need for OptionsData, ModifiersData, or ModifiersMappingData storage
- Options fetched fresh in Phase 2 (no storage needed)
- Modifiers have no dependents (no mapping storage needed)
- Only option mappings needed for Phase 3 variant creation

**Files to Modify:**
- `src/BigCommerce.Migration.Core/Models/StorageModels.cs`

---

### **P2-T2: Implement Options Fetch Strategy** 🔧
**Priority**: High | **Effort**: 4 hours | **Dependencies**: P2-T1

**Sub-tasks:**
1. **P2-T2.1**: Create `OptionsFetchStrategy.cs`
   ```csharp
   public class OptionsFetchStrategy : IEntityFetchStrategy
   {
       public string EntityType => "options";
       
       public async Task<List<Dictionary<string, object>>> FetchEntitiesAsync(...)
       {
           // Fetch products in chunks of 10 with include=options,modifiers
           var products = await _apiClient.GetProductsAsync(
               sourceStore, 
               page, 
               10,  // API limit when including options
               "options,modifiers",
               cancellationToken);
               
           // Extract all options from all products
           var allOptions = new List<Dictionary<string, object>>();
           foreach (var product in products)
           {
               if (product.TryGetValue("options", out var optionsValue))
               {
                   // Extract individual options with product context
                   var options = ExtractOptionsWithProductContext(product, optionsValue);
                   allOptions.AddRange(options);
               }
           }
           return allOptions;
       }
   }
   ```

2. **P2-T2.2**: Handle pagination for 10 products/page
3. **P2-T2.3**: Extract options with product ID context

**Configuration:**
```json
{
  "options": {
    "chunkSize": 10,
    "fetchBatchSize": 10,
    "pageSize": 10,
    "subBatchSize": 20,
    "maxConcurrency": 15,
    "include": "options,modifiers"
  }
}
```

**Files to Create:**
- `src/BigCommerce.Migration.Orchestration/Strategies/OptionsFetchStrategy.cs`

---

### **P2-T3: Implement Options Transform Strategy** 🔧
**Priority**: High | **Effort**: 3 hours | **Dependencies**: P2-T2

**Sub-tasks:**
1. **P2-T3.1**: Create `OptionsTransformStrategy.cs`
   ```csharp
   public class OptionsTransformStrategy : IEntityTransformStrategy
   {
       public async Task<Dictionary<string, object>> TransformEntityAsync(...)
       {
           // Transform source option to destination format
           var transformed = new Dictionary<string, object>();
           
           // Map option properties
           transformed["display_name"] = GetStringValue(entity, "display_name");
           transformed["type"] = GetStringValue(entity, "type");
           transformed["required"] = GetBooleanValue(entity, "required");
           
           // Transform option values
           if (entity.TryGetValue("option_values", out var optionValuesValue))
           {
               transformed["option_values"] = TransformOptionValues(optionValuesValue);
           }
           
           return transformed;
       }
   }
   ```

2. **P2-T3.2**: Handle option values transformation
3. **P2-T3.3**: Validate required fields

**Files to Create:**
- `src/BigCommerce.Migration.Orchestration/Strategies/OptionsTransformStrategy.cs`

---

### **P2-T4: Implement Options Creation Strategy** 🔧
**Priority**: High | **Effort**: 4 hours | **Dependencies**: P2-T3

**Sub-tasks:**
1. **P2-T4.1**: Create `OptionsCreationStrategy.cs`
   ```csharp
   public class OptionsCreationStrategy : IEntityCreationStrategy
   {
       public async Task<string> CreateEntityAsync(...)
       {
           // Get destination product ID from mapping
           var productMapping = await GetProductMappingAsync(sourceProductId, migrationId);
           
           // Create option via POST /catalog/products/{product_id}/options
           var response = await _apiClient.CreateProductOptionAsync(
               productMapping.DestinationId, 
               transformedOptionData);
           
           var optionId = response["id"].ToString();
           
           // Store option mapping for variants phase
           await StoreOptionMappingAsync(sourceOptionId, optionId, productMapping.DestinationId, migrationId);
           
           return optionId;
       }
   }
   ```

2. **P2-T4.2**: Implement parallel option creation (20 concurrent)
3. **P2-T4.3**: Store option ID mappings for Phase 3
4. **P2-T4.4**: Handle option values creation
5. **P2-T4.5**: Error handling and retry logic

**Files to Create:**
- `src/BigCommerce.Migration.Orchestration/Strategies/OptionsCreationStrategy.cs`

---

### **P2-T5: Implement Modifiers (Similar to Options)** 🔧
**Priority**: High | **Effort**: 8 hours | **Dependencies**: P2-T4

**Sub-tasks:**
1. **P2-T5.1**: Create `ModifiersFetchStrategy.cs` (similar to options)
2. **P2-T5.2**: Create `ModifiersTransformStrategy.cs` (similar to options)
3. **P2-T5.3**: Create `ModifiersCreationStrategy.cs` (similar to options)
4. **P2-T5.4**: Store modifier ID mappings

**Files to Create:**
- `src/BigCommerce.Migration.Orchestration/Strategies/ModifiersFetchStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Strategies/ModifiersTransformStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Strategies/ModifiersCreationStrategy.cs`

---

## 🔄 **PHASE 3: VARIANTS MIGRATION**

### **P3-T1: Enhance Variants Fetch Strategy** 🔧
**Priority**: High | **Effort**: 3 hours | **Dependencies**: P2 Complete

**Sub-tasks:**
1. **P3-T1.1**: Enhance existing `VariantsFetchStrategy.cs`
   ```csharp
   public async Task<List<Dictionary<string, object>>> FetchEntitiesAsync(...)
   {
       // Fetch variants with normal pagination (250/page)
       var variants = await _apiClient.GetProductVariantsAsync(
           sourceStore,
           page,
           250,  // Normal pagination without includes
           cancellationToken);
       
       return variants;
   }
   ```

2. **P3-T1.2**: Implement proper pagination (250 variants/page)
3. **P3-T1.3**: Add product context to variants

**Configuration:**
```json
{
  "variants": {
    "chunkSize": 250,
    "fetchBatchSize": 250,
    "pageSize": 250,
    "subBatchSize": 50,
    "maxConcurrency": 5,
    "batchCreate": true
  }
}
```

**Files to Modify:**
- `src/BigCommerce.Migration.Orchestration/Strategies/VariantsFetchStrategy.cs`

---

### **P3-T2: Enhance Variants Transform Strategy** 🔧
**Priority**: High | **Effort**: 4 hours | **Dependencies**: P3-T1

**Sub-tasks:**
1. **P3-T2.1**: Enhance `VariantsTransformStrategy.cs` with option mappings
   ```csharp
   public async Task<Dictionary<string, object>> TransformEntityAsync(...)
   {
       // Get option mappings from EntityMapping
       var optionMappings = await GetOptionMappingsAsync(productId, migrationId);
       
       // Transform option_values using mappings
       var transformedOptionValues = new List<object>();
       foreach (var optionValue in sourceVariant.OptionValues)
       {
           if (optionMappings.TryGetValue(optionValue.OptionId, out var destinationOptionId))
           {
               transformedOptionValues.Add(new {
                   option_id = destinationOptionId,
                   option_value_id = await GetOptionValueMappingAsync(optionValue.Id, migrationId)
               });
           }
           else
           {
               // Handle missing mapping gracefully
               _logger.LogWarning("Option mapping not found for option {OptionId}, skipping for variant {VariantId}", 
                   optionValue.OptionId, sourceVariant.Id);
           }
       }
       
       transformed["option_values"] = transformedOptionValues;
       return transformed;
   }
   ```

2. **P3-T2.2**: Implement option value mapping lookups
3. **P3-T2.3**: Handle missing mappings gracefully (don't fail entire batch)
4. **P3-T2.4**: Transform variant pricing and inventory

**Files to Modify:**
- `src/BigCommerce.Migration.Orchestration/Strategies/VariantsTransformStrategy.cs`

---

### **P3-T3: Enhance Variants Creation Strategy** 🔧
**Priority**: High | **Effort**: 5 hours | **Dependencies**: P3-T2

**Sub-tasks:**
1. **P3-T3.1**: Enhance `VariantsCreationStrategy.cs` with batch creation
   ```csharp
   public async Task<List<string>> CreateBatchAsync(List<Dictionary<string, object>> variants)
   {
       // Group by product_id for batch creation
       var variantsByProduct = variants.GroupBy(v => v["product_id"].ToString());
       
       var createdVariantIds = new List<string>();
       
       foreach (var productVariants in variantsByProduct)
       {
           var batches = productVariants.Chunk(50); // BigCommerce batch limit
           
           foreach (var batch in batches)
           {
               // POST /catalog/products/{product_id}/variants with batch payload
               var batchResponse = await _apiClient.CreateVariantsBatchAsync(
                   productVariants.Key, 
                   batch.ToList());
               
               // Handle batch response and extract created IDs
               var batchIds = ExtractCreatedIds(batchResponse);
               createdVariantIds.AddRange(batchIds);
           }
       }
       
       return createdVariantIds;
   }
   ```

2. **P3-T3.2**: Implement 50 variants per batch creation
3. **P3-T3.3**: Handle batch creation errors gracefully
4. **P3-T3.4**: Store variant mappings
5. **P3-T3.5**: Implement parallel batch processing (5 concurrent batches)

**Files to Modify:**
- `src/BigCommerce.Migration.Orchestration/Strategies/VariantsCreationStrategy.cs`

---

## 📸 **PHASE 4: IMAGES MIGRATION**

### **P4-T1: Implement Images Fetch Strategy** 🔧
**Priority**: Medium | **Effort**: 4 hours | **Dependencies**: P1 Complete

**Sub-tasks:**
1. **P4-T1.1**: Create `ImagesFetchStrategy.cs`
   ```csharp
   public class ImagesFetchStrategy : IEntityFetchStrategy
   {
       public async Task<List<Dictionary<string, object>>> FetchEntitiesAsync(...)
       {
           var allImages = new List<Dictionary<string, object>>();
           
           // Get product IDs from EntityMapping
           var productMappings = await _entityMappingService.GetEntityMappingsAsync(migrationId, "products");
           
           // Fetch images for each product individually (parallel)
           var semaphore = new SemaphoreSlim(20); // Control concurrency
           
           var tasks = productMappings.Select(async mapping =>
           {
               await semaphore.WaitAsync();
               try
               {
                   // GET /catalog/products/{product_id}/images
                   var images = await _apiClient.GetProductImagesAsync(mapping.SourceId);
                   foreach (var image in images)
                   {
                       image["source_product_id"] = mapping.SourceId;
                       image["destination_product_id"] = mapping.DestinationId;
                   }
                   return images;
               }
               finally
               {
                   semaphore.Release();
               }
           });
           
           var results = await Task.WhenAll(tasks);
           return results.SelectMany(r => r).ToList();
       }
   }
   ```

2. **P4-T1.2**: Implement high concurrency (20 parallel) for individual fetches
3. **P4-T1.3**: Handle API rate limiting and errors

**Configuration:**
```json
{
  "images": {
    "chunkSize": 100,
    "fetchBatchSize": 1,
    "pageSize": 1,
    "subBatchSize": 10,
    "maxConcurrency": 20,
    "individualFetch": true
  }
}
```

**Files to Create:**
- `src/BigCommerce.Migration.Orchestration/Strategies/ImagesFetchStrategy.cs`

---

### **P4-T2: Implement Images Transform Strategy** 🔧
**Priority**: Medium | **Effort**: 2 hours | **Dependencies**: P4-T1

**Sub-tasks:**
1. **P4-T2.1**: Create `ImagesTransformStrategy.cs`
2. **P4-T2.2**: Handle image URL transformation
3. **P4-T2.3**: Map image metadata and properties

**Files to Create:**
- `src/BigCommerce.Migration.Orchestration/Strategies/ImagesTransformStrategy.cs`

---

### **P4-T3: Implement Images Creation Strategy** 🔧
**Priority**: Medium | **Effort**: 3 hours | **Dependencies**: P4-T2

**Sub-tasks:**
1. **P4-T3.1**: Create `ImagesCreationStrategy.cs` with individual creation
2. **P4-T3.2**: Handle image upload and creation
3. **P4-T3.3**: Implement error resilience for individual images
4. **P4-T3.4**: High concurrency (20 parallel) for image creation

**Files to Create:**
- `src/BigCommerce.Migration.Orchestration/Strategies/ImagesCreationStrategy.cs`

---

## 📝 **PHASE 5: REVIEWS MIGRATION**

### **P5-T1: Implement Reviews Migration Strategy** 🔧
**Priority**: Medium | **Effort**: 4 hours | **Dependencies**: P1 Complete

**Sub-tasks:**
1. **P5-T1.1**: Create `ReviewsFetchStrategy.cs`
   ```csharp
   public class ReviewsFetchStrategy : IEntityFetchStrategy
   {
       public async Task<List<Dictionary<string, object>>> FetchEntitiesAsync(...)
       {
           var allReviews = new List<Dictionary<string, object>>();
           
           // Get product IDs from EntityMapping
           var productMappings = await _entityMappingService.GetEntityMappingsAsync(migrationId, "products");
           
           // Read reviews from blob storage (stored in Phase 1)
           foreach (var mapping in productMappings)
           {
               var reviewsData = await _blobStorageService.ReadReviewsDataAsync(migrationId, mapping.SourceId);
               if (reviewsData != null)
               {
                   var reviews = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(reviewsData);
                   foreach (var review in reviews)
                   {
                       review["source_product_id"] = mapping.SourceId;
                       review["destination_product_id"] = mapping.DestinationId;
                   }
                   allReviews.AddRange(reviews);
               }
           }
           
           return allReviews;
       }
   }
   ```

2. **P5-T1.2**: Create `ReviewsTransformStrategy.cs`
3. **P5-T1.3**: Create `ReviewsCreationStrategy.cs` with high concurrency (20 parallel)
4. **P5-T1.4**: Implement blob storage service for reading review data

**Configuration:**
```json
{
  "reviews": {
    "chunkSize": 100,
    "fetchBatchSize": 1,
    "pageSize": 1,
    "subBatchSize": 10,
    "maxConcurrency": 20,
    "individualCreate": true,
    "blobStorageContainer": "reviews-data"
  }
}
```

**Files to Create:**
- `src/BigCommerce.Migration.Orchestration/Strategies/ReviewsFetchStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Strategies/ReviewsTransformStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Strategies/ReviewsCreationStrategy.cs`
- `src/BigCommerce.Migration.Infrastructure/Services/ReviewsBlobStorageService.cs`

---

## 🔗 **PHASE 6: CHANNEL ASSIGNMENTS**

### **P6-T1: Implement Channel Assignment Strategy** 🔧
**Priority**: Low | **Effort**: 3 hours | **Dependencies**: P1 Complete

**Sub-tasks:**
1. **P6-T1.1**: Create `ChannelAssignmentStrategy.cs`
   ```csharp
   public class ChannelAssignmentStrategy
   {
       public async Task ProcessChannelAssignmentsAsync(string migrationId)
       {
           // Get products with channel data from EntityMapping
           var productMappings = await _entityMappingService.GetEntityMappingsAsync(migrationId, "products");
           var productsWithChannels = productMappings.Where(m => !string.IsNullOrEmpty(m.ChannelsData));
           
           foreach (var mapping in productsWithChannels)
           {
               var channelsData = JsonSerializer.Deserialize<List<object>>(mapping.ChannelsData);
               
               // Map source channels to destination channels
               var destinationChannels = await MapChannelsAsync(channelsData, migrationId);
               
               // POST /catalog/products/channel-assignments
               await _apiClient.CreateProductChannelAssignmentsAsync(
                   mapping.DestinationId, 
                   destinationChannels);
           }
       }
   }
   ```

2. **P6-T1.2**: Implement channel mapping logic
3. **P6-T1.3**: Handle batch channel assignments

**Files to Create:**
- `src/BigCommerce.Migration.Orchestration/Strategies/ChannelAssignmentStrategy.cs`

---

## 🔄 **PHASE 7: RELATED PRODUCTS UPDATE**

### **P7-T1: Implement Related Products Update Strategy** 🔧
**Priority**: Low | **Effort**: 3 hours | **Dependencies**: P1 Complete

**Sub-tasks:**
1. **P7-T1.1**: Create `RelatedProductsUpdateStrategy.cs`
   ```csharp
   public class RelatedProductsUpdateStrategy
   {
       public async Task UpdateRelatedProductsAsync(string migrationId)
       {
           var productMappings = await _entityMappingService.GetEntityMappingsAsync(migrationId, "products");
           var productsWithRelated = productMappings.Where(m => !string.IsNullOrEmpty(m.RelatedProductsData));
           
           var batches = productsWithRelated.Chunk(10); // BigCommerce batch limit
           
           foreach (var batch in batches)
           {
               var updatePayload = batch.Select(mapping => new {
                   id = int.Parse(mapping.DestinationId),
                   related_products = MapRelatedProductIds(mapping.RelatedProductsData, migrationId)
               }).ToList();
               
               // PUT /catalog/products (batch update)
               await _apiClient.UpdateProductsBatchAsync(updatePayload);
           }
       }
   }
   ```

2. **P7-T1.2**: Implement product ID mapping for related products
3. **P7-T1.3**: Handle batch updates (10 products per batch)

**Files to Create:**
- `src/BigCommerce.Migration.Orchestration/Strategies/RelatedProductsUpdateStrategy.cs`

---

## 📊 **PHASE 8: PRODUCT METAFIELDS**

### **P8-T1: Implement Metafields Migration** 🔧
**Priority**: Low | **Effort**: 4 hours | **Dependencies**: P1 Complete

**Sub-tasks:**
1. **P8-T1.1**: Create `MetafieldsFetchStrategy.cs`
   ```csharp
   public async Task<List<Dictionary<string, object>>> FetchEntitiesAsync(...)
   {
       // GET /catalog/products/metafields (batch get)
       var allMetafields = await _apiClient.GetAllProductMetafieldsAsync(
           sourceStore,
           page,
           pageSize,
           cancellationToken);
       
       return allMetafields;
   }
   ```

2. **P8-T1.2**: Create `MetafieldsTransformStrategy.cs`
3. **P8-T1.3**: Create `MetafieldsCreationStrategy.cs` with batch creation
   ```csharp
   public async Task<List<string>> CreateBatchAsync(List<Dictionary<string, object>> metafields)
   {
       // Transform metafields with product ID mappings
       var transformedMetafields = await TransformMetafieldsAsync(metafields, migrationId);
       
       // Create in batches of 50
       var batches = transformedMetafields.Chunk(50);
       
       var createdIds = new List<string>();
       foreach (var batch in batches)
       {
           // POST /catalog/products/metafields (batch create)
           var response = await _apiClient.CreateMultipleMetafieldsAsync(batch.ToList());
           createdIds.AddRange(ExtractCreatedIds(response));
       }
       
       return createdIds;
   }
   ```

**Configuration:**
```json
{
  "metafields": {
    "chunkSize": 200,
    "fetchBatchSize": 200,
    "pageSize": 200,
    "subBatchSize": 50,
    "maxConcurrency": 8,
    "batchCreate": true
  }
}
```

**Files to Create:**
- `src/BigCommerce.Migration.Orchestration/Strategies/MetafieldsFetchStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Strategies/MetafieldsTransformStrategy.cs`
- `src/BigCommerce.Migration.Orchestration/Strategies/MetafieldsCreationStrategy.cs`

---

## 🔄 **DEPENDENCY ORDER UPDATE**

### **D-T1: Configuration-Driven Entity Order** ⭐
**Priority**: Low | **Effort**: 1 hour | **Dependencies**: P2 Complete

**Approach**: Use configuration to control entity dependency order. Start with baseline implementation (brands + products), then add enhanced-products when ready.

**Sub-tasks:**
1. **D-T1.1**: Update `MigrationDurableOrchestrator.cs` (if needed)
   ```csharp
   // CURRENT BASELINE (Working)
   var fullDependencyOrder = new[]
   {
       "brands",              // ✅ Current working implementation
       "products"             // ✅ Current working implementation
   };
   
   // ENHANCED (Future Phase)
   var enhancedDependencyOrder = new[]
   {
       "brands",              // Must be before products
       "products",            // Standard products (250/page)
       "enhanced-products"    // ✅ NEW: Comprehensive with sub-entities (10/page)
       // Sub-entities (options, modifiers, images, reviews) processed WITHIN enhanced-products
   };
   ```

**Files to Modify:**
- `src/BigCommerce.Migration.Functions/Orchestrators/MigrationDurableOrchestrator.cs`

---

## 🧪 **TESTING TASKS**

### **T-T1: Phase 1 Testing** 🧪
**Priority**: High | **Effort**: 4 hours | **Dependencies**: P1 Complete

**Test Categories:**
- ✅ Enhanced includes don't break existing product migration
- ✅ All 5 include types captured correctly
- ✅ Create payload integration working
- ✅ Performance regression testing (maintain 250/page)
- ✅ New metadata stored in EntityMapping

### **T-T2: Phase 2-3 Testing** 🧪
**Priority**: High | **Effort**: 6 hours | **Dependencies**: P2-P3 Complete

**Test Categories:**
- ✅ Options created with correct mappings
- ✅ Variants created with proper option_values
- ✅ Missing option mappings handled gracefully
- ✅ Batch creation efficiency validation
- ✅ 10/page vs 250/page performance comparison

### **T-T3: Phase 4-7 Testing** 🧪
**Priority**: Medium | **Effort**: 4 hours | **Dependencies**: P4-P7 Complete

**Test Categories:**
- ✅ Individual image migration resilience
- ✅ Channel assignment accuracy
- ✅ Related products relationship integrity
- ✅ Metafields batch processing efficiency
- ✅ End-to-end migration validation

---

## 📈 **SUCCESS METRICS BY PHASE**

### **Phase 1 Success Criteria:**
- Products migration maintains 100% success rate
- All 5 include types captured: bulk_pricing_rules, custom_fields, channels, videos, reviews
- Create payload correctly integrates bulk_pricing_rules, videos, custom_fields
- No performance regression (maintain 250/page throughput)

### **Phase 2 Success Criteria:**
- Options and modifiers created with 95%+ success rate
- Option mappings stored correctly for Phase 3
- 10 products/page throughput maintained (API limitation accepted)
- Parallel creation efficiency: 20 concurrent option creations

### **Phase 3 Success Criteria:**
- Variants created with 95%+ success rate using option mappings
- Batch creation efficiency: 50 variants per API call
- Missing option mappings handled gracefully (don't fail batches)
- 250 variants/page fetch performance maintained

### **Phase 4 Success Criteria:**
- Images migrated with 90%+ success rate (individual failures acceptable)
- High concurrency achieved: 20 parallel image operations
- Individual image failures don't affect other images

### **Phase 5-7 Success Criteria:**
- Channel assignments: 100% accuracy
- Related products: 100% relationship integrity
- Metafields: 95%+ success rate with batch efficiency
- Overall migration: Zero data loss across all phases

---

## 🚨 **RISK MITIGATION**

### **Performance Risks:**
- **Phase 2 Bottleneck**: 10/page limit for options accepted as necessary trade-off
- **Phase 4 Scaling**: High concurrency needed for individual image fetching
- **Memory Usage**: Process in appropriate chunks for each phase

### **Data Integrity Risks:**
- **Missing Mappings**: Handle gracefully in Phase 3 (create variants without options)
- **Relationship Integrity**: Validate mappings before dependent entity creation
- **Batch Failures**: Implement partial success handling

### **API Rate Limiting:**
- **High Volume**: Monitor rate limits especially in Phase 4 (images)
- **Concurrent Operations**: Implement proper semaphore controls
- **Retry Logic**: Exponential backoff for failed operations

---

*This task breakdown provides comprehensive guidance for implementing all 7 phases of the enhanced product migration strategy with detailed acceptance criteria and success metrics.* 🚀