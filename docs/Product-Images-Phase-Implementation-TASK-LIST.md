# Product-Images Phase Implementation - Task List

## 📋 **Phase Overview**
- **Phase Name**: Product-Images
- **Phase Number**: 4 (after Product-Related, before Variants)
- **API Approach**: Individual Product Update (`PUT /v3/catalog/products/{product_id}`)
- **Image Limit**: Maximum 1000 images per product
- **Chunk Size**: 50 images per API call
- **Skip Logic**: Products with zero images are skipped

## 🎯 **Implementation Strategy**
- Use Individual Product Update API for better error isolation
- Process images in 50-image chunks to respect API limits
- Fetch images using streaming approach to manage memory
- Follow ProductRelated pattern for EntityMappings-based discovery
- Track individual image success/failure for comprehensive reporting

---

## 📋 **TASK BREAKDOWN**

### **Task 1: Phase Configuration Setup**
**Priority**: High | **Dependencies**: None | **Estimated Time**: 1 hour

#### **Subtasks**:
1.1. **Add Product-Images Phase Configuration**
   - Update `EntityDependencyResolver.InitializePhaseConfigurations()`
   - Add phase configuration with correct phase number (4)
   - Set appropriate PageSize: 20, Include: "", target entity types: ["products"]

1.2. **Add Sub-Batch Configuration**
   - Update `appsettings.json` ParallelProcessing.subBatchConfigurations
   - Add product-images with validated timeout-safe settings:
     ```json
     "product-images": {
       "entityType": "product-images",
       "chunkSize": 20,
       "fetchBatchSize": 50,
       "pageSize": 20,
       "subBatchSize": 1,
       "maxConcurrency": 5,
       "enableSubBatching": true,
       "subBatchDelayMs": 50,
       "processSubBatchesSequentially": false,
       "imageChunkSize": 50,
       "imageChunkDelayMs": 300,
       "maxImagesPerProduct": 1000,
       "productTimeoutSeconds": 120,
       "include": ""
     }
     ```

1.3. **Update Product Phase List**
   - Modify `GetEntityPhases()` to include "product-images" after "product-related"
   - Ensure correct phase ordering and numbering (shifts variants to phase 5)

1.4. **Configuration Validation**
   - Verify phase configuration is properly loaded
   - Test phase ordering in dependency resolver
   - Validate timeout constraints are within 10-minute function limit

#### **Deliverables**:
- Modified `EntityDependencyResolver.cs` with new phase configuration
- Updated `appsettings.json` with sub-batch configuration
- Updated phase list with correct ordering

#### **Acceptance Criteria**:
- [ ] Phase configuration loads correctly with PageSize: 20
- [ ] Phase appears in correct position (4) after product-related
- [ ] Sub-batch configuration includes all validated timeout-safe settings
- [ ] Configuration supports 5 concurrent products with 300ms chunk delays
- [ ] Function timeout constraints validated (worst case: 55.7s < 10min limit)

---

### **Task 2: Discovery Strategy Implementation**
**Priority**: High | **Dependencies**: Task 1 | **Estimated Time**: 3 hours

#### **Subtasks**:
2.1. **Create ProductImagesDiscoveryStrategy**
   - Implement `IEntityDiscoveryStrategy` interface
   - Follow ProductRelatedDiscoveryStrategy pattern
   - Query EntityMappings table for products with source/destination IDs

2.2. **Integrate with DiscoveryStrategyFactory**
   - Add product-images case to strategy factory
   - Ensure proper strategy selection and instantiation

2.3. **Discovery Testing**
   - Test EntityMappings querying functionality
   - Verify products are discovered correctly
   - Test pagination of product mappings

#### **Deliverables**:
- `ProductImagesDiscoveryStrategy.cs` in Discovery folder
- Updated `EntityDiscoveryStrategyFactory.cs`

#### **Acceptance Criteria**:
- [ ] Discovery strategy queries EntityMappings correctly
- [ ] Returns appropriate product source/destination ID pairs
- [ ] Integrates properly with discovery factory
- [ ] Follows established patterns from ProductRelatedDiscoveryStrategy

---

### **Task 3: Image Fetching Service**
**Priority**: High | **Dependencies**: Task 2 | **Estimated Time**: 4 hours

#### **Subtasks**:
3.1. **Create ProductImagesFetchService**
   - Implement `IProductImagesFetchService` interface for fetching product images
   - Add streaming fetch with pagination (fetchBatchSize: 50 images per page)
   - Implement 1000 image limit enforcement (maxImagesPerProduct: 1000)
   - Add proper error handling and logging with structured data

3.2. **API Integration**
   - Use `/v3/catalog/products/{product_id}/images?page=X&limit=50` endpoint
   - Handle pagination correctly with while loop until limit reached or no more data
   - Implement proper cancellation support and timeout handling
   - Respect rate limiting and API constraints

3.3. **Memory Management**
   - Implement streaming approach to avoid loading 1000 images at once
   - Fetch and return images in configurable batches (50 per call)
   - Ensure memory footprint stays under 1MB per concurrent product

#### **Deliverables**:
- `IProductImagesFetchService.cs` interface
- `ProductImagesFetchService.cs` implementation
- Service registration in DI container

#### **Acceptance Criteria**:
- [ ] Fetches images using proper pagination (50 per page)
- [ ] Enforces 1000 image limit per product
- [ ] Handles products with zero images gracefully
- [ ] Implements proper error handling and cancellation
- [ ] Memory efficient - doesn't load all images at once

---

### **Task 4: Transform Strategy Adaptation**
**Priority**: Medium | **Dependencies**: Task 3 | **Estimated Time**: 3 hours

#### **Subtasks**:
4.1. **Create ProductImagesTransformStrategy**
   - Adapt existing ImageTransformStrategy for bulk arrays
   - Handle transformation of image collections (not individual images)
   - Maintain product context for destination ID mapping

4.2. **Bulk Transformation Logic**
   - Transform arrays of images while preserving sort order
   - Handle image URL formatting and validation
   - Map product_id from source to destination

4.3. **Integration with Transform Factory**
   - Add product-images case to EntityTransformStrategyFactory
   - Ensure proper strategy selection

#### **Deliverables**:
- `ProductImagesTransformStrategy.cs` 
- Updated `EntityTransformStrategyFactory.cs`
- Modified existing ImageTransformStrategy if needed

#### **Acceptance Criteria**:
- [x] Transforms arrays of images correctly ✅
- [x] Preserves image sort order from source ✅
- [x] Maps product_id to destination correctly ✅
- [x] Handles image URL transformation properly ✅ (Virtual path construction)
- [x] Integrates with transform strategy factory ✅
- [x] **Additional**: Comprehensive unit testing (28/28 tests passing) ✅
- [x] **Additional**: Virtual path URL construction with fallback ✅
- [x] **Additional**: Required fields validation and defaults ✅

---

### **Task 5: Creation Strategy Implementation**
**Priority**: High | **Dependencies**: Task 4 | **Estimated Time**: 6 hours

#### **Subtasks**:
5.1. **Create ProductImagesCreationStrategy**
   - Implement `IEntityCreationStrategy` interface
   - Use individual product processing with SubBatchProcessor (subBatchSize: 1)
   - Implement chunked image updates (imageChunkSize: 50 images per API call)
   - Use validated timeout-safe configuration (maxConcurrency: 5)

5.2. **Individual Product Processing**
   - Process each product independently using SubBatchProcessor pattern
   - Fetch images for product using ProductImagesFetchService (max 1000 images)
   - Handle products with zero images (skip with tracking and logging)
   - Implement productTimeoutSeconds: 120 per product

5.3. **Chunked Image Updates**
   - Split product images into 50-image chunks (BigCommerce API limit)
   - Make separate `PUT /v3/catalog/products/{product_id}` calls for each chunk
   - Handle append logic for multiple chunks per product with proper sequencing
   - Implement imageChunkDelayMs: 300 between chunks for rate limiting

5.4. **Error Handling and Progress Tracking**
   - Track individual image success/failure counts per product
   - Continue processing on individual image failures (continue-on-error policy)
   - Log errors to OpenSearch without retry logic as specified
   - Implement proper cancellation support throughout processing

5.5. **Integration with Creation Factory**
   - Add product-images case to EntityCreationStrategyFactory
   - Ensure proper strategy selection and instantiation
   - Verify compatibility with existing entity processing pipeline

#### **Deliverables**:
- `ProductImagesCreationStrategy.cs`
- Updated `EntityCreationStrategyFactory.cs`
- Comprehensive error handling and logging

#### **Acceptance Criteria**:
- [x] Processes products individually using SubBatchProcessor (subBatchSize: 1, maxConcurrency: 5) ✅
- [x] Handles chunked updates for products with >50 images (imageChunkSize: 50) ✅
- [x] Implements 300ms delay between image chunks (imageChunkDelayMs: 300) ✅
- [x] Skips products with zero images and tracks them properly ✅
- [x] Implements proper error handling without retry logic as specified ✅
- [x] Tracks individual image success/failure counts per product ✅
- [x] Uses correct API endpoint (`PUT /v3/catalog/products/{product_id}`) ✅
- [x] Respects timeout constraints (productTimeoutSeconds: 120, worst case: 55.7s) ✅
- [x] Maintains memory efficiency (<1MB per concurrent product) ✅
- [x] **Enhanced**: Implements `_skip` marker handling similar to ProductRelatedCreationStrategy ✅
- [x] **Enhanced**: ProductImagesTransformStrategy sets `_skip` markers for products with no images ✅
- [x] **Enhanced**: Comprehensive skip tracking and reporting with detailed statistics ✅

---

### **Task 6: Pipeline Integration**
**Priority**: Medium | **Dependencies**: Task 5 | **Estimated Time**: 2 hours

#### **Subtasks**:
6.1. **Update ProcessEntityChunkActivity**
   - Add routing for "product-images" entity type
   - Ensure proper pipeline routing to standard processing

6.2. **Verify Entity Service Integration**
   - Test EntityTransformService integration
   - Test EntityCreateService integration
   - Verify proper strategy factory usage

#### **Deliverables**:
- Updated `ProcessEntityChunkActivity.cs` with product-images routing
- Verified pipeline integration

#### **Acceptance Criteria**:
- [x] Product-images entities route through standard pipeline ✅
- [x] Transform and creation services work correctly ✅
- [x] Proper strategy selection and execution ✅

---

### **Task 7: Configuration and Settings**
**Priority**: Low | **Dependencies**: Task 6 | **Estimated Time**: 1 hour

#### **Subtasks**:
7.1. **Verify Application Settings**
   - Confirm product-images configuration in appsettings.json (completed in Task 1)
   - Validate timeout-safe settings are properly applied
   - Test configuration loading and parsing

7.2. **Performance Configuration Validation**
   - Verify sub-batch settings work with SubBatchProcessor
   - Test concurrent processing limits (maxConcurrency: 5)
   - Validate memory usage stays within acceptable limits
   - Confirm chunk delays are properly implemented (imageChunkDelayMs: 300)

#### **Deliverables**:
- Validated configuration loading and parsing
- Performance settings verification report
- Memory and timeout constraint validation

#### **Acceptance Criteria**:
- [ ] Configuration properly loaded and applied from appsettings.json
- [ ] Performance settings validated for timeout-safe operation (worst case: 55.7s < 120s limit)
- [ ] Sub-batch configuration works correctly (subBatchSize: 1, maxConcurrency: 5)
- [ ] Memory usage validated to stay under 1MB per concurrent product
- [ ] Chunk delay implementation verified (300ms between image chunks)

---

### **Task 8: Testing and Validation**
**Priority**: High | **Dependencies**: Task 7 | **Estimated Time**: 4 hours

#### **Subtasks**:
8.1. **Unit Testing**
   - Test ProductImagesDiscoveryStrategy with EntityMappings
   - Test ProductImagesFetchService with 1000 image limit and pagination
   - Test ProductImagesTransformStrategy with array transformation
   - Test ProductImagesCreationStrategy with chunked processing and timeout constraints

8.2. **Integration Testing**
   - Test complete phase workflow with timeout validation
   - Test with products having various image counts (0, 25, 75, 1000+)
   - Test error scenarios and continue-on-error policy
   - Test concurrent processing with 5 products simultaneously
   - Test chunked updates with 300ms delays

8.3. **Performance Testing**
   - Test memory usage with large image sets (target: <1MB per product)
   - Test concurrent processing performance (5 products simultaneous)
   - Validate chunked processing efficiency and timeout safety (worst case: 55.7s)
   - Test function timeout compliance (10-minute limit)
   - Validate rate limiting and API constraint compliance

#### **Deliverables**:
- Comprehensive unit test suite
- Integration tests covering various scenarios
- Performance validation results

#### **Acceptance Criteria**:
- [ ] All unit tests pass with >90% code coverage
- [ ] Integration tests cover key scenarios (0, 25, 75, 1000+ images)
- [ ] Performance meets validated requirements (worst case: 55.7s < 120s timeout)
- [ ] Memory usage remains efficient (<1MB per concurrent product)
- [ ] Error handling works correctly with continue-on-error policy
- [ ] Concurrent processing validated (5 products simultaneous)
- [ ] Function timeout compliance verified (10-minute limit)
- [ ] Rate limiting and chunk delays working properly (300ms)

---

### **Task 9: Documentation and Cleanup**
**Priority**: Low | **Dependencies**: Task 8 | **Estimated Time**: 2 hours

#### **Subtasks**:
9.1. **Code Documentation**
   - Add comprehensive XML documentation
   - Update architecture documentation
   - Document API usage patterns

9.2. **Phase Documentation**
   - Update product migration workflow documentation
   - Document new phase in migration guide

#### **Deliverables**:
- Complete code documentation
- Updated architecture documentation

#### **Acceptance Criteria**:
- [ ] All classes have proper XML documentation
- [ ] Architecture documentation is updated
- [ ] Migration workflow documentation includes new phase

---

## 📊 **Task Dependencies Graph**

```
Task 1 (Config) → Task 2 (Discovery) → Task 3 (Fetch Service) → Task 4 (Transform) → Task 5 (Creation) → Task 6 (Pipeline) → Task 7 (Settings) → Task 8 (Testing) → Task 9 (Documentation)
```

## ⏱️ **Estimated Total Time**: 26 hours

## 🚀 **Ready for Approval**

This task list is ready for your review and approval. Each task has clear:
- Subtasks with specific deliverables
- Acceptance criteria for validation
- Dependencies and time estimates
- Clear scope boundaries

Please review and approve before we proceed with implementation.
