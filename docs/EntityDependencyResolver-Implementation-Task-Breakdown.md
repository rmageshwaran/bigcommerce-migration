# Entity Dependency Resolver Implementation - Task Breakdown

## 📋 Document Overview

**Purpose**: Implement intelligent entity dependency resolution to automatically include dependencies and dependents when users select entities for migration.

**Current Problem**: 
- Users must manually select all dependencies (e.g., categories, brands) when they want to migrate products
- System only processes explicitly requested entities, leading to mapping failures
- No automatic resolution of dependent entities (e.g., images, variants for products)

**Desired Solution**:
- User selects only "products" → System automatically includes "categories", "brands", "products", "images", "variants"
- Smart dependency resolution based on BigCommerce entity relationships
- Maintains processing order while including all required entities

**Complexity Assessment**: **MEDIUM** (Architecture exists in documentation, integration point is simple, requires implementing 4-5 classes)

---

## 🎯 Architecture Analysis

### Current Implementation (Lines 441-460 in MigrationDurableOrchestrator.cs):
```csharp
private static List<string> GetEntityDependencyOrder(IEnumerable<string> requestedEntities)
{
    var fullDependencyOrder = new[]
    {
        "categories",  // Must be first - referenced by products
        "brands",      // Must be before products - referenced by products  
        "products",    // Must be before variants and images
        "variants",    // Depends on products
        "images",      // Can reference products and variants
        "modifiers"    // Product modifiers/options
    };

    // ❌ ONLY processes explicitly requested entities
    var requestedSet = new HashSet<string>(requestedEntities, StringComparer.OrdinalIgnoreCase);
    
    return fullDependencyOrder
        .Where(entity => requestedSet.Contains(entity))  // ⚠️ FILTERS to requested only
        .ToList();
}
```

### Target Implementation (Based on Migration-Architecture-and-Execution-Flow.md):
```csharp
// ✅ New: Auto-includes dependencies + dependents  
var entityResolution = _entityDependencyResolver.ResolveEntities(
    input.MigrationRequest?.Entities ?? new List<string>(),
    new EntityResolutionOptions { IncludeDependants = true }
);
var entityOrder = entityResolution.ResolvedEntities;
```

### Integration Points:
1. **MigrationDurableOrchestrator.cs** (Line 156): Replace `GetEntityDependencyOrder` call
2. **MigrationOrchestrator.cs** (Lines 169-171): Replace entity filtering logic
3. **Dependency Injection**: Register new services in `ServiceCollectionExtensions.cs`

---

## 📁 File Structure Analysis

### Files to Create:
```
src/BigCommerce.Migration.Core/
├── Interfaces/
│   ├── IEntityDependencyResolver.cs
│   └── IEntityDependencyConfiguration.cs
└── Services/
    ├── EntityDependencyResolver.cs
    ├── EntityDependencyConfiguration.cs
    └── Models/
        ├── EntityResolutionResult.cs
        ├── EntityResolutionOptions.cs
        ├── DependencyStep.cs
        └── DependencyType.cs
```

### Files to Modify:
```
src/BigCommerce.Migration.Functions/Orchestrators/
├── MigrationDurableOrchestrator.cs (Line 156)

src/BigCommerce.Migration.Orchestration/Orchestrators/
├── MigrationOrchestrator.cs (Lines 169-171)

src/BigCommerce.Migration.Infrastructure/Extensions/
├── ServiceCollectionExtensions.cs (Dependency injection)
```

---

## 🔧 Implementation Tasks

### **Phase 1: Core Classes Implementation** (4-6 hours)

#### **Task 1.1: Create Interfaces** (1 hour)
- **Files**: `IEntityDependencyResolver.cs`, `IEntityDependencyConfiguration.cs`
- **Priority**: High
- **Dependencies**: None

**Implementation Details**:
```csharp
// Based on Migration-Architecture-and-Execution-Flow.md lines 714-720
public interface IEntityDependencyConfiguration
{
    List<string> GetDependencies(string entity);
    List<string> GetDependants(string entity);
    Dictionary<string, int> GetEntityPriorities();
    bool IsOptionalDependency(string entity, string dependency);
}

public interface IEntityDependencyResolver
{
    EntityResolutionResult ResolveEntities(List<string> requestedEntities, EntityResolutionOptions options = null);
}
```

#### **Task 1.2: Create Supporting Models** (1 hour)
- **Files**: `EntityResolutionResult.cs`, `EntityResolutionOptions.cs`, `DependencyStep.cs`, `DependencyType.cs`
- **Priority**: High
- **Dependencies**: Task 1.1

**Implementation Details**:
```csharp
// Based on Migration-Architecture-and-Execution-Flow.md lines 850-871
public class EntityResolutionResult
{
    public List<string> RequestedEntities { get; set; } = new();
    public List<string> ResolvedEntities { get; set; } = new();
    public List<List<string>> PriorityGroups { get; set; } = new();
    public List<DependencyStep> DependencyChain { get; set; } = new();
    public EntityResolutionOptions ResolutionOptions { get; set; }
}

public enum DependencyType { Required, Optional, Dependant }
```

#### **Task 1.3: Implement EntityDependencyConfiguration** (2 hours)
- **File**: `EntityDependencyConfiguration.cs`
- **Priority**: High
- **Dependencies**: Task 1.1, 1.2

**Entity Relationships to Implement**:
```csharp
// Based on current system analysis and BigCommerce API relationships
private readonly Dictionary<string, EntityConfiguration> _entityConfigurations = new()
{
    ["categories"] = new EntityConfiguration
    {
        Priority = 1,
        Dependencies = new List<string>(), // No dependencies
        Dependants = new List<string> { "products" }
    },
    ["brands"] = new EntityConfiguration
    {
        Priority = 1,
        Dependencies = new List<string>(), // No dependencies
        Dependants = new List<string> { "products" }
    },
    ["products"] = new EntityConfiguration
    {
        Priority = 2,
        Dependencies = new List<string> { "categories", "brands" },
        Dependants = new List<string> { "variants", "images", "modifiers" }
    },
    ["variants"] = new EntityConfiguration
    {
        Priority = 3,
        Dependencies = new List<string> { "products" },
        Dependants = new List<string>()
    },
    ["images"] = new EntityConfiguration
    {
        Priority = 3,
        Dependencies = new List<string> { "products" },
        Dependants = new List<string>()
    },
    ["modifiers"] = new EntityConfiguration
    {
        Priority = 3,
        Dependencies = new List<string> { "products" },
        Dependants = new List<string>()
    }
};
```

#### **Task 1.4: Implement EntityDependencyResolver** (2-3 hours)
- **File**: `EntityDependencyResolver.cs`
- **Priority**: High
- **Dependencies**: Task 1.1, 1.2, 1.3

**Key Methods to Implement**:
```csharp
// Based on Migration-Architecture-and-Execution-Flow.md lines 599-707
public EntityResolutionResult ResolveEntities(List<string> requestedEntities, EntityResolutionOptions options = null)
{
    options ??= new EntityResolutionOptions { IncludeDependants = true, IncludeOptionalDependencies = false };
    
    var resolvedEntities = new HashSet<string>(requestedEntities);
    var dependencyChain = new List<DependencyStep>();
    
    // Step 1: Add all required dependencies recursively
    foreach (var entity in requestedEntities)
    {
        var dependencies = ResolveDependenciesRecursively(entity, new HashSet<string>());
        foreach (var dependency in dependencies)
        {
            if (resolvedEntities.Add(dependency))
            {
                dependencyChain.Add(new DependencyStep
                {
                    Entity = dependency,
                    Reason = $"Required dependency for {entity}",
                    Type = DependencyType.Required
                });
            }
        }
    }
    
    // Step 2: Add dependants if requested
    if (options.IncludeDependants)
    {
        var currentEntities = resolvedEntities.ToList();
        foreach (var entity in currentEntities)
        {
            var dependants = GetDependants(entity);
            foreach (var dependant in dependants)
            {
                if (resolvedEntities.Add(dependant))
                {
                    dependencyChain.Add(new DependencyStep
                    {
                        Entity = dependant,
                        Reason = $"Dependant of {entity}",
                        Type = DependencyType.Dependant
                    });
                }
            }
        }
    }
    
    // Step 3: Order entities by priority
    var priorityGroups = GroupEntitiesByPriority(resolvedEntities.ToList());
    
    return new EntityResolutionResult
    {
        RequestedEntities = requestedEntities,
        ResolvedEntities = resolvedEntities.ToList(),
        PriorityGroups = priorityGroups,
        DependencyChain = dependencyChain,
        ResolutionOptions = options
    };
}
```

### **Phase 2: Integration with Existing System** (2 hours)

#### **Task 2.1: Register Dependencies** (30 minutes)
- **File**: `src/BigCommerce.Migration.Infrastructure/Extensions/ServiceCollectionExtensions.cs`
- **Priority**: High
- **Dependencies**: Phase 1 complete

**Implementation**:
```csharp
// Add after existing service registrations
services.AddScoped<IEntityDependencyConfiguration, EntityDependencyConfiguration>();
services.AddScoped<IEntityDependencyResolver, EntityDependencyResolver>();
```

#### **Task 2.2: Update MigrationDurableOrchestrator** (45 minutes)
- **File**: `src/BigCommerce.Migration.Functions/Orchestrators/MigrationDurableOrchestrator.cs`
- **Priority**: High
- **Dependencies**: Task 2.1

**Changes Required**:
```csharp
// Line 156: Replace current implementation
// OLD:
var entityOrder = GetEntityDependencyOrder(input.MigrationRequest?.Entities ?? new List<string>());

// NEW:
var entityResolution = await context.CallActivityAsync<EntityResolutionResult>(
    "ResolveEntityDependencies", 
    input.MigrationRequest?.Entities ?? new List<string>()
);
var entityOrder = entityResolution.ResolvedEntities;

// Log the resolution result
logger.LogInformation("Entity dependency resolution for MigrationId {MigrationId}: " +
    "Requested={RequestedEntities}, Resolved={ResolvedEntities}, " +
    "DependencyChain={DependencyChain}",
    migrationId,
    string.Join(",", entityResolution.RequestedEntities),
    string.Join(",", entityResolution.ResolvedEntities),
    string.Join(" -> ", entityResolution.DependencyChain.Select(d => $"{d.Entity}({d.Type})"))
);
```

#### **Task 2.3: Create ResolveEntityDependencies Activity** (45 minutes)
- **File**: `src/BigCommerce.Migration.Functions/Functions/DependencyResolutionFunctions.cs` (new)
- **Priority**: High
- **Dependencies**: Task 2.1

**Implementation**:
```csharp
[Function("ResolveEntityDependencies")]
public async Task<EntityResolutionResult> ResolveEntityDependencies(
    [ActivityTrigger] List<string> requestedEntities,
    FunctionContext context)
{
    var logger = context.GetLogger("ResolveEntityDependencies");
    
    try
    {
        var result = _entityDependencyResolver.ResolveEntities(
            requestedEntities,
            new EntityResolutionOptions { IncludeDependants = true }
        );
        
        logger.LogInformation("Resolved entities: Requested={RequestedCount}, Resolved={ResolvedCount}",
            result.RequestedEntities.Count, result.ResolvedEntities.Count);
            
        return result;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to resolve entity dependencies for entities: {Entities}",
            string.Join(",", requestedEntities));
        throw;
    }
}
```

### **Phase 3: Update Legacy Orchestrator (Optional)** (1 hour)

#### **Task 3.1: Update MigrationOrchestrator** (1 hour)
- **File**: `src/BigCommerce.Migration.Orchestration/Orchestrators/MigrationOrchestrator.cs`
- **Priority**: Medium
- **Dependencies**: Phase 2 complete

**Changes Required**:
```csharp
// Lines 167-171: Replace entity filtering
// OLD:
var requestedEntities = request.OriginalRequest.Entities.ToList();
var entitiesToProcess = EntityProcessingOrder
    .Where(entity => requestedEntities.Contains(entity, StringComparer.OrdinalIgnoreCase))
    .ToList();

// NEW:
var entityResolution = _entityDependencyResolver.ResolveEntities(
    request.OriginalRequest.Entities.ToList(),
    new EntityResolutionOptions { IncludeDependants = true }
);
var entitiesToProcess = entityResolution.ResolvedEntities;

// Add dependency injection in constructor
private readonly IEntityDependencyResolver _entityDependencyResolver;

public MigrationOrchestrator(
    ILogger<MigrationOrchestrator> logger, 
    IProgressEventPublisher progressEventPublisher, 
    ISignalREventFactory signalREventFactory,
    IEntityDependencyResolver entityDependencyResolver)
{
    _logger = logger;
    _progressEventPublisher = progressEventPublisher;
    _signalREventFactory = signalREventFactory;
    _entityDependencyResolver = entityDependencyResolver ?? throw new ArgumentNullException(nameof(entityDependencyResolver));
}
```

### **Phase 4: Testing and Validation** (2-3 hours)

#### **Task 4.1: Unit Tests** (2 hours)
- **Files**: `tests/BigCommerce.Migration.UnitTests/Core/Services/EntityDependencyResolverTests.cs` (new)
- **Priority**: High
- **Dependencies**: Phase 1, 2 complete

**Test Cases to Implement**:
```csharp
[Test]
public void ResolveEntities_ProductsOnly_IncludesCategoriesAndBrands()
{
    // Arrange
    var requestedEntities = new List<string> { "products" };
    
    // Act
    var result = _resolver.ResolveEntities(requestedEntities);
    
    // Assert
    Assert.That(result.ResolvedEntities, Contains.Item("categories"));
    Assert.That(result.ResolvedEntities, Contains.Item("brands"));
    Assert.That(result.ResolvedEntities, Contains.Item("products"));
    Assert.That(result.ResolvedEntities, Contains.Item("variants"));
    Assert.That(result.ResolvedEntities, Contains.Item("images"));
}

[Test]
public void ResolveEntities_BrandsOnly_IncludesDependants()
{
    // Test for automatic dependant inclusion
}

[Test]
public void ResolveEntities_CircularDependency_ThrowsException()
{
    // Test circular dependency detection
}

[Test]
public void ResolveEntities_MultipleEntities_CorrectOrder()
{
    // Test processing order is maintained
}
```

#### **Task 4.2: Integration Testing** (1 hour)
- **File**: `tests/BigCommerce.Migration.IntegrationTests/DependencyResolutionIntegrationTests.cs` (new)
- **Priority**: Medium
- **Dependencies**: Task 4.1

**Test Scenarios**:
- End-to-end migration with only "products" selected
- Verify all dependencies are created before products
- Verify dependents are created after products
- Performance testing with large entity sets

### **Phase 5: Documentation and Cleanup** (1 hour)

#### **Task 5.1: Update Documentation** (30 minutes)
- **Files**: Update existing docs to reflect implementation status
- **Priority**: Low
- **Dependencies**: All phases complete

#### **Task 5.2: Remove Legacy Methods** (30 minutes)
- **File**: Remove old `GetEntityDependencyOrder` method after confirming new system works
- **Priority**: Low
- **Dependencies**: Testing complete

---

## 🎯 Success Criteria

### **Functional Requirements**:
- [ ] User selects "products" → System includes "categories", "brands", "products", "images", "variants"
- [ ] User selects "brands" → System includes "brands", "products", "images", "variants"
- [ ] Processing order maintains dependencies (categories/brands before products, products before images/variants)
- [ ] No breaking changes to existing explicit entity selections
- [ ] All 562+ existing tests continue to pass

### **Non-Functional Requirements**:
- [ ] EntityDependencyResolver execution time < 100ms for typical entity sets
- [ ] Memory usage within current system limits
- [ ] Proper error handling for circular dependencies
- [ ] Comprehensive logging of dependency resolution decisions

### **User Experience Improvements**:
- [ ] Frontend form can be simplified (users only select what they want)
- [ ] No more mapping failures due to missing dependencies
- [ ] Clear dependency chain logging for troubleshooting

---

## 📊 Risk Assessment

### **Low Risk**:
- ✅ Architecture already designed and documented
- ✅ Clear integration point (single line change)
- ✅ No external dependencies required
- ✅ Backward compatibility maintained

### **Medium Risk**:
- ⚠️ Need to ensure deterministic behavior in Durable Functions
- ⚠️ Proper error handling for invalid entity types
- ⚠️ Performance impact of dependency resolution

### **Mitigation Strategies**:
- **Determinism**: Implement as Activity Function (already planned in Task 2.3)
- **Error Handling**: Comprehensive validation and logging in EntityDependencyResolver
- **Performance**: Cache dependency configurations, simple graph traversal algorithms

---

## ⏱️ Timeline Summary

| **Phase** | **Tasks** | **Estimated Time** | **Dependencies** |
|-----------|-----------|-------------------|------------------|
| **Phase 1** | Core Classes (1.1-1.4) | **4-6 hours** | None |
| **Phase 2** | Integration (2.1-2.3) | **2 hours** | Phase 1 complete |
| **Phase 3** | Legacy Update (3.1) | **1 hour** | Phase 2 complete |
| **Phase 4** | Testing (4.1-4.2) | **2-3 hours** | Phase 1-2 complete |
| **Phase 5** | Documentation (5.1-5.2) | **1 hour** | All phases complete |
| **Total** | **16 tasks** | **10-13 hours** | **Medium complexity** |

---

## 🔧 Implementation Order

### **Day 1: Core Implementation** (6-8 hours)
1. Task 1.1: Create Interfaces
2. Task 1.2: Create Supporting Models  
3. Task 1.3: Implement EntityDependencyConfiguration
4. Task 1.4: Implement EntityDependencyResolver
5. Task 2.1: Register Dependencies

### **Day 2: Integration & Testing** (4-5 hours)
1. Task 2.2: Update MigrationDurableOrchestrator
2. Task 2.3: Create ResolveEntityDependencies Activity
3. Task 4.1: Unit Tests
4. Task 4.2: Integration Testing

### **Day 3: Completion** (1-2 hours)
1. Task 3.1: Update MigrationOrchestrator (optional)
2. Task 5.1: Update Documentation
3. Task 5.2: Remove Legacy Methods

---

## 🎯 Next Steps

1. **Approve this task breakdown** and confirm priorities
2. **Start with Phase 1** - Core classes implementation
3. **Use parallel development** - Multiple tasks can be worked on simultaneously
4. **Test incrementally** - Validate each phase before proceeding
5. **Deploy gradually** - Feature flag to enable/disable new system during rollout

This implementation will significantly improve the user experience by making the system "smart enough" to automatically handle entity dependencies and dependents, eliminating the need for users to understand BigCommerce's complex entity relationships.