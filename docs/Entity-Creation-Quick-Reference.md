# Entity Creation Quick Reference

## 🚀 Quick Decision Tree

### Adding a New Entity Type?

**❓ Is this entity independent or does it belong to another entity?**

#### ✅ Independent Entity (Pipeline 1)
**Examples**: Products, Brands, Categories, Variants  
**API Pattern**: `POST /v3/catalog/{entities}`

**Steps**:
1. Create `{EntityName}CreationStrategy` implementing `IEntityCreationStrategy`
2. Register in `ServiceCollectionExtensions.cs`
3. Add to `EntityFetchStrategyFactory` normalization (if needed)
4. Will be processed via `ProcessEntityChunkActivity`

#### ✅ Dependent Entity (Pipeline 2)  
**Examples**: Options, Modifiers, Images, Reviews  
**API Pattern**: `POST /v3/catalog/products/{id}/{entities}`

**Steps**:
1. Create `{EntityName}CreationStrategy` implementing `IEntityCreationStrategy`
2. Register in `ServiceCollectionExtensions.cs`
3. Add to `EntityDependencyResolver` as a component phase
4. Will be processed via `ProductComponentsMigrationPipeline`

## 📋 Entity Classification Reference

### Pipeline 1: Independent Entities
```
✅ Products      → ProductCreationStrategy      → POST /v3/catalog/products
✅ Brands        → BrandCreationStrategy        → POST /v3/catalog/brands  
✅ Categories    → CategoryCreationStrategy     → POST /v3/catalog/categories
✅ Variants      → VariantCreationStrategy      → POST /v3/catalog/products/{id}/variants
```

### Pipeline 2: Dependent Entities
```
✅ Options       → OptionsCreationStrategy      → POST /v3/catalog/products/{id}/options
✅ Modifiers     → ModifierCreationStrategy     → POST /v3/catalog/products/{id}/modifiers
✅ Images        → ImageCreationStrategy        → POST /v3/catalog/products/{id}/images
✅ Reviews       → ReviewsCreationStrategy      → POST /v3/catalog/products/{id}/reviews
```

## 🔧 Implementation Templates

### Independent Entity Strategy Template
```csharp
public class {EntityName}CreationStrategy : IEntityCreationStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<{EntityName}CreationStrategy> _logger;

    public string EntityType => "{entityname}";

    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities,
        string migrationId,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        // API call to: POST /v3/catalog/{entityname}
        // Direct entity creation
    }
}
```

### Dependent Entity Strategy Template  
```csharp
public class {EntityName}CreationStrategy : IEntityCreationStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<{EntityName}CreationStrategy> _logger;
    private readonly IMigrationStorageService _migrationStorageService;

    public string EntityType => "{entityname}";

    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities,
        string migrationId,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        // API call to: POST /v3/catalog/products/{parent_id}/{entityname}
        // Requires parent entity mapping lookup
        
        foreach (var entity in entities)
        {
            // 1. Get parent entity ID from mapping
            var parentId = await GetDestinationParentId(sourceParentId, migrationId);
            
            // 2. Prepare payload for sub-entity
            var payload = PrepareEntityPayload(entity, parentId);
            
            // 3. Call API with parent context
            // POST /v3/catalog/products/{parentId}/{entityname}
        }
    }
}
```

## 🎯 Registration Checklist

### ✅ For Any New Entity Strategy:
1. **Implement Interface**: `IEntityCreationStrategy`
2. **Register in DI**: `ServiceCollectionExtensions.cs`
3. **Add Logging**: Use consistent logging patterns
4. **Error Handling**: Implement proper exception handling
5. **Entity Mapping**: Store source→destination ID relationships

### ✅ Additional for Pipeline 1 (Independent):
- Add to `EntityFetchStrategyFactory` normalization (if needed)
- Consider batch size optimization
- Add to `EntityDependencyResolver` if it has dependents

### ✅ Additional for Pipeline 2 (Dependent):
- Add parent entity mapping lookup
- Add to appropriate phase in `EntityDependencyResolver`
- Implement payload transformation for parent context

## 🚦 Processing Flow Reference

### When Migration Request is "products":
```
1. EntityDependencyResolver detects "products"
2. Returns 6-phase sequence: [products, product-components, variants, ...]
3. Phase 1: ProcessEntityChunkActivity → ProductCreationStrategy
4. Phase 2: ProductComponentsMigrationPipeline → [Options,Modifiers,Images,Reviews]CreationStrategy
5. Phase 3-6: ProcessEntityChunkActivity → respective strategies
```

### When Migration Request is "brands":
```
1. EntityDependencyResolver detects "brands"  
2. Returns single-phase: [brands]
3. Phase 1: ProcessEntityChunkActivity → BrandCreationStrategy
```

## 📊 Performance Guidelines

### Pipeline 1 Targets:
- **Products**: 250 entities/page
- **Brands**: 50 entities/page  
- **Categories**: 50 entities/page
- **Target**: 12,000+ req/hour

### Pipeline 2 Targets:
- **All Components**: 10 products/page (with includes)
- **Focus**: Relationship integrity over raw speed
- **Target**: <5% error rate

## 🔍 Debugging Tips

### Pipeline 1 Issues:
- Check `ProcessEntityChunkActivity` logs
- Verify entity fetch strategy
- Check API rate limiting

### Pipeline 2 Issues:
- Check `ProductComponentsMigrationPipeline` logs  
- Verify parent entity mappings exist
- Check include parameters in fetch

### Common Issues:
- **Missing parent mapping**: Component entities need existing products
- **API endpoint mismatch**: Verify independent vs dependent API pattern
- **DI registration**: Ensure strategy is registered in ServiceCollection

---

**Quick Reference Version**: 1.0  
**For**: Developers adding new entity types  
**Architecture**: Dual-Pipeline Strategy Pattern