# Product Migration Workflow Guide

## Overview
This document outlines the complete product migration workflow in the BigCommerce Migration system, including the detailed flow for products, product components, and variants.

## Migration Phases

### Phase 1: Products Migration
The foundation phase that creates the basic product entities.

```
1. Discovery → Count products
   - Strategy: V3EfficientPaginationStrategy
   - Purpose: Get total product count for progress tracking
   - API: GET /v3/catalog/products (no includes)

2. Fetch → Get products (basic data, no includes)
   - Strategy: Direct pagination
   - Data: Basic product fields only
   - Performance: Fast due to no includes

3. Transform → Map product fields
   - Strategy: ProductTransformStrategy
   - Processing: Category mappings, brand mappings, field transformations
   - Dependencies: Requires categories and brands to be migrated first

4. Create → Create products in destination store
   - Strategy: ProductCreationStrategy
   - API: POST/PUT /v3/catalog/products
   - Result: Creates basic product structure with default variants

5. Mapping → Store product ID mappings
   - Purpose: Map source product IDs to destination product IDs
   - Storage: EntityMapping table
   - Usage: Required for components and variants phases
```

### Phase 2: Product Components Migration
After products are created, migrate their components **in parallel**.

#### Supported Component Types
- **`options`** - Product options/variants configuration
- **`modifiers`** - Product modifiers/add-ons  
- **`images`** - Product images
- **`reviews`** - Product reviews

#### Entity Expansion Logic
The system automatically expands component dependencies:

```
User selects: ["products"]
↓
EntityDependencyResolver adds: ["products", "product-components"]
↓
ResolveEntityDependenciesActivity expands: ["products", "options", "modifiers", "images", "reviews"]
```

This ensures individual progress tracking for each component type.

#### Component Migration Flow
Each component type follows this workflow **in parallel**:

```
1. Discovery → Count components
   - Strategy: V3ProductComponentsDiscoveryStrategy
   - Process:
     * Fetch products with include=options,modifiers,images,reviews
     * Parse and count specific component type (e.g., count images across all products)
     * Return total count for progress tracking
     * Discard fetched data (discovery phase only counts)

2. Fetch → Get products with includes
   - Strategy: EntityFetchService with component detection
   - Process:
     * Detect component type (images, options, modifiers, reviews)
     * Fetch products with include=options,modifiers,images,reviews
     * Limit: 10/page (BigCommerce API constraint with includes)
     * Return: Full product data with embedded components

3. Transform → Extract & transform component
   - ImageTransformStrategy: Extract images array from product.images
   - OptionsTransformStrategy: Extract options array from product.options
   - ModifierTransformStrategy: Extract modifiers array from product.modifiers
   - ReviewsTransformStrategy: Extract reviews array from product.reviews
   - Purpose: Convert embedded component data to standalone entities

4. Create → Create component in destination
   - ImageCreationStrategy: POST /v3/catalog/products/{id}/images
   - OptionsCreationStrategy: POST /v3/catalog/products/{id}/options
   - ModifierCreationStrategy: POST /v3/catalog/products/{id}/modifiers
   - ReviewsCreationStrategy: POST to appropriate reviews endpoint
   - Dependencies: Requires product ID mappings from Phase 1

5. Processing → Standard parallel processing pipeline
   - Pipeline: ProcessStandardEntitiesForChunk
   - Parallelism: All component types process simultaneously
   - Progress: Individual tracking per component type in entityprogress table
```

### Phase 3: Product Variants Migration
After components are created, migrate additional product variants.

```
1. Discovery → Count variants
   - Strategy: Standard variant discovery
   - Purpose: Count total variants across all products

2. Fetch → Get variants by product
   - Strategy: Standard variant fetch
   - Data: Variant-specific fields and options

3. Transform → Map variant fields
   - Strategy: VariantTransformStrategy
   - Logic: Handle variant-specific transformations
   - Dependencies: Requires option mappings from Phase 2

4. Create → Create variants with duplicate detection
   - Strategy: VariantCreationStrategy
   - Logic:
     * Skip default variants (already created with product in Phase 1)
     * Create only additional variants
     * Handle empty batch scenarios (all variants skipped)
     * Count skipped variants as successes

5. Mapping → Store variant ID mappings
   - Purpose: Map source variant IDs to destination variant IDs
   - Usage: Required for future variant-dependent entities
```

## Architecture Details

### Dependency Order
```
Products (Phase 1)
    ↓
Components (Phase 2) - All types in parallel
    ↓ 
Variants (Phase 3)
```

### Processing Strategies

#### Direct Pagination Strategy
- **Used for**: Products, Variants, standard entities
- **Characteristics**: Fast, no includes, standard page limits
- **API Pattern**: GET /v3/catalog/{entity}?page=X&limit=250

#### Component Fetch Strategy  
- **Used for**: Images, Options, Modifiers, Reviews
- **Characteristics**: Slower, includes required, limited to 10/page
- **API Pattern**: GET /v3/catalog/products?page=X&limit=10&include=options,modifiers,images,reviews

### Parallel Processing

#### Component Parallelism
- All 4 component types process simultaneously
- No interdependencies between component types
- Each component has independent progress tracking
- Failures in one component don't affect others

#### Error Handling Strategy
- **Infrastructure errors**: Stop entire migration (network, auth, etc.)
- **Component errors**: Log and continue processing other components
- **Skipped entities**: Count as successes (e.g., duplicate default variants)
- **User visibility**: Only show actionable BigCommerce API errors to users

### Progress Tracking

#### Entity Progress Table
Each component type gets its own row in the `entityprogress` table:
- `images`: Track image migration progress
- `options`: Track options migration progress  
- `modifiers`: Track modifiers migration progress
- `reviews`: Track reviews migration progress

#### Count Accuracy
- **Discovery**: Counts total components across all products
- **Processing**: Processes components individually for accurate progress
- **Dashboard**: Shows individual component progress, not grouped

## API Limitations & Considerations

### BigCommerce API Constraints
- **With includes**: Maximum 10 products per page
- **Without includes**: Up to 250 entities per page
- **Rate limiting**: 5-50 requests/second based on dynamic throttling

### Performance Optimizations
- **Products**: No includes for maximum speed (250/page)
- **Components**: Includes required but parallel processing compensates
- **Batching**: Sub-batch processing for optimal API utilization

## Error Scenarios & Handling

### Common Issues
1. **Empty variant batches**: All variants skipped as duplicates
   - **Solution**: Skip API call, return success results
   - **Counting**: Mark skipped as successful

2. **Component count mismatches**: Dashboard shows wrong counts
   - **Solution**: Ensure discovery counts match processing expectations
   - **Validation**: Individual component type tracking

3. **Missing fetch strategies**: Component types not supported
   - **Solution**: Use product fetch with component detection
   - **Architecture**: No individual component fetch strategies needed

### Debugging Tools
- **Migration logs**: Track fetch, transform, create steps
- **Progress tracking**: Monitor individual component progress
- **Error categorization**: Distinguish between user and system errors
- **Payload logging**: Store request/response for failed operations

## Testing Strategy

### Unit Tests
- Component extraction from product data
- Transform strategy logic for each component type
- Creation strategy API calls and mapping

### Integration Tests  
- End-to-end component migration workflow
- Parallel processing validation
- Error handling and recovery
- Progress tracking accuracy

### Performance Tests
- Component fetch with includes (10/page limitation)
- Parallel processing throughput
- Memory usage with embedded component data

---

**Last Updated**: January 2025  
**Version**: 1.0  
**Author**: BigCommerce Migration System Documentation