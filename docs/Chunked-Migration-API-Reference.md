# 📚 Chunked Hierarchical Category Migration - API Reference

## 🎯 **Overview**

This document provides comprehensive API documentation for the Chunked Hierarchical Category Migration system, designed to transform memory-intensive category migration (200MB+) into memory-safe chunked processing (max 25MB) while achieving 5-10x performance improvement via BigCommerce bulk creation API optimization.

---

## 🔧 **Core Interfaces**

### **IChunkedHierarchicalDiscoveryStrategy**

The primary interface for chunked hierarchical category discovery, following Interface Segregation and Dependency Inversion principles.

```csharp
namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for chunked hierarchical category discovery
/// Follows Interface Segregation Principle - focused on chunked discovery only
/// Follows Dependency Inversion Principle - high-level abstraction
/// </summary>
public interface IChunkedHierarchicalDiscoveryStrategy : IEntityDiscoveryStrategy
{
    /// <summary>
    /// Analyzes hierarchy structure without loading all data into memory
    /// Returns metadata only for memory safety
    /// </summary>
    /// <param name="sourceStore">Source BigCommerce store configuration</param>
    /// <param name="categoryTreeId">Optional category tree ID to analyze</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Hierarchy metadata with level counts and structure info</returns>
    Task<HierarchyMetadata> AnalyzeHierarchyStructureAsync(
        StoreConfiguration sourceStore,
        string? categoryTreeId,
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// Counts categories at a specific hierarchy level
    /// Memory-safe operation - count only, no data loading
    /// </summary>
    /// <param name="sourceStore">Source BigCommerce store configuration</param>
    /// <param name="categoryTreeId">Optional category tree ID</param>
    /// <param name="level">Hierarchy level to count (0 = root)</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Number of categories at the specified level</returns>
    Task<int> CountCategoriesAtLevelAsync(
        StoreConfiguration sourceStore,
        string? categoryTreeId,
        int level,
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// Validates if chunked processing should be used based on category count
    /// </summary>
    /// <param name="sourceStore">Source BigCommerce store configuration</param>
    /// <param name="categoryTreeId">Optional category tree ID</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>True if chunked processing is recommended</returns>
    Task<bool> ShouldUseChunkedProcessingAsync(
        StoreConfiguration sourceStore,
        string? categoryTreeId,
        CancellationToken cancellationToken = default);
}
```

---

## 🏗️ **Core Implementation**

### **ChunkedHierarchicalDiscoveryStrategy**

Memory-safe hierarchical discovery strategy implementation.

```csharp
namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Memory-safe hierarchical discovery strategy
/// Follows Single Responsibility Principle - only handles chunked discovery
/// Follows Open/Closed Principle - extensible for new hierarchy types
/// </summary>
public class ChunkedHierarchicalDiscoveryStrategy : IChunkedHierarchicalDiscoveryStrategy
{
    // Constructor with dependency injection
    public ChunkedHierarchicalDiscoveryStrategy(
        IBigCommerceApiClient apiClient,
        ISignalREventFactory signalREventFactory,
        ILogger<ChunkedHierarchicalDiscoveryStrategy> logger,
        IOptions<ChunkedHierarchyConfiguration> configuration)
    
    // Main discovery method - Azure Durable Functions compatible
    public async Task<EntityDiscoveryResult> DiscoverEntitiesAsync(
        EntityDiscoveryRequest request, 
        CancellationToken cancellationToken = default)
}
```

#### **Key Features:**
- ✅ **Memory Safety**: Max 25MB memory usage per operation
- ✅ **Performance**: 5-10x faster via BigCommerce bulk creation API
- ✅ **Deterministic**: Azure Durable Functions compliant
- ✅ **Error Handling**: Continue-on-error policy with graceful fallbacks
- ✅ **Cancellation**: Full cancellation token support

---

## 📊 **Data Models**

### **HierarchyMetadata**

Contains metadata about the category hierarchy structure.

```csharp
/// <summary>
/// Metadata about hierarchy structure for memory-safe processing
/// </summary>
public class HierarchyMetadata
{
    /// <summary>Total number of categories in hierarchy</summary>
    public int TotalCount { get; set; }
    
    /// <summary>Maximum depth level found</summary>
    public int MaxLevel { get; set; }
    
    /// <summary>Number of categories per level</summary>
    public Dictionary<int, int> LevelCounts { get; set; } = new();
    
    /// <summary>Peak memory usage during analysis (MB)</summary>
    public double PeakMemoryUsageMB { get; set; }
    
    /// <summary>Analysis duration in milliseconds</summary>
    public long AnalysisDurationMs { get; set; }
    
    /// <summary>Whether chunked processing is recommended</summary>
    public bool RecommendChunkedProcessing { get; set; }
}
```

### **CategoryLevelRequest**

Request model for processing a specific category level.

```csharp
/// <summary>
/// Request model for processing a specific category level
/// Azure Durable Functions compatible
/// </summary>
public class CategoryLevelRequest
{
    public string MigrationId { get; set; } = "";
    public int Level { get; set; }
    public List<Dictionary<string, object>> Categories { get; set; } = new();
    public StoreConfiguration DestinationStore { get; set; } = new();
    public CategoryTreeContext? CategoryTreeContext { get; set; }
    public Dictionary<string, string> ParentIdMappings { get; set; } = new();
    
    /// <summary>
    /// Validates request for Azure Durable Functions
    /// </summary>
    public bool IsValid() { /* validation logic */ }
}
```

---

## ⚙️ **Activities**

### **FetchCategoriesForLevelActivity**

Azure Function activity for fetching categories at a specific hierarchy level.

```csharp
[Function("FetchCategoriesForLevelActivity")]
public async Task<LevelFetchActivityResult> FetchCategoriesForLevelAsync(
    [ActivityTrigger] CategoryLevelRequest request)
```

**Features:**
- Memory-safe level-by-level fetching
- Chunked processing to respect memory limits
- Cancellation support
- Comprehensive error handling

### **ProcessCategoryLevelActivity**  

Azure Function activity for processing/creating categories at a specific level.

```csharp
[Function("ProcessCategoryLevelActivity")]
public async Task<LevelProcessingActivityResult> ProcessCategoryLevelAsync(
    [ActivityTrigger] CategoryLevelRequest request)
```

**Features:**
- BigCommerce bulk creation API integration
- 96% reduction in HTTP calls
- Continue-on-error policy
- Parent ID mapping management

---

## 🎛️ **Factory Pattern**

### **EntityDiscoveryStrategyFactory**

Factory for selecting appropriate discovery strategy based on configuration.

```csharp
/// <summary>
/// Determines which discovery strategy to use for categories
/// </summary>
public bool ShouldUseChunkedStrategy(
    string entityType, 
    ChunkedHierarchyConfiguration config)
{
    // Primary decision: UseChunkedCategoryMigration feature flag
    if (config.UseChunkedCategoryMigration.HasValue)
    {
        return config.UseChunkedCategoryMigration.Value;
    }
    
    // Fallback: Use chunked for categories by default
    return entityType.Equals("categories", StringComparison.OrdinalIgnoreCase);
}
```

---

## 🔄 **Orchestration**

### **ChunkedCategoryMigrationOrchestrator**

Azure Durable Functions orchestrator for chunked category migration.

```csharp
[Function("ChunkedCategoryMigrationOrchestrator")]
public async Task<ChunkedMigrationResult> RunChunkedMigrationAsync(
    [OrchestrationTrigger] TaskOrchestrationContext context, 
    ChunkedMigrationRequest request)
```

**Flow:**
1. **Discovery Phase**: Analyze hierarchy structure
2. **Level Processing**: Process each level sequentially  
3. **Bulk Creation**: Use BigCommerce bulk API for performance
4. **Progress Tracking**: Real-time SignalR updates
5. **Error Handling**: Continue-on-error with comprehensive logging

---

## 📈 **Performance Metrics**

### **BigCommerce Bulk Creation API Optimization**

**Performance Impact:**
- **Before**: 1,000 categories = 1,000 API calls (20 seconds at 50 req/sec)
- **After**: 1,000 categories = 40 API calls (0.8 seconds at 50 req/sec)  
- **Improvement**: 96% reduction in API calls = 25x faster creation phase

### **Memory Usage Targets**
- **Maximum Memory**: 25MB per processing chunk
- **Typical Usage**: 10-15MB per level processing
- **Memory Monitoring**: Built-in tracking and alerts

### **Processing Speed Targets**
- **Small Datasets** (≤1,000 categories): 2-3x improvement
- **Medium Datasets** (1,000-10,000 categories): 5-7x improvement  
- **Large Datasets** (10,000+ categories): 8-10x improvement

---

## 🔧 **Usage Examples**

### **Basic Discovery**

```csharp
// Inject the strategy
var strategy = serviceProvider.GetRequiredService<IChunkedHierarchicalDiscoveryStrategy>();

// Analyze hierarchy
var metadata = await strategy.AnalyzeHierarchyStructureAsync(
    sourceStore: sourceStoreConfig,
    categoryTreeId: null, // Use default tree
    cancellationToken: cancellationToken);

// Check if chunked processing is recommended
if (metadata.RecommendChunkedProcessing)
{
    Console.WriteLine($"Chunked processing recommended for {metadata.TotalCount} categories");
    Console.WriteLine($"Max hierarchy depth: {metadata.MaxLevel}");
}
```

### **Count Categories at Level**

```csharp
// Count root categories (level 0)
var rootCount = await strategy.CountCategoriesAtLevelAsync(
    sourceStore: sourceStoreConfig,
    categoryTreeId: null,
    level: 0,
    cancellationToken: cancellationToken);

Console.WriteLine($"Found {rootCount} root categories");
```

### **Strategy Factory Usage**

```csharp
// Get appropriate strategy via factory
var factory = serviceProvider.GetRequiredService<IEntityDiscoveryStrategyFactory>();
var strategy = factory.GetStrategy("categories");

// The factory automatically selects chunked strategy based on configuration
if (strategy is IChunkedHierarchicalDiscoveryStrategy chunkedStrategy)
{
    // Use chunked-specific methods
    var shouldUseChunked = await chunkedStrategy.ShouldUseChunkedProcessingAsync(
        sourceStoreConfig, null, cancellationToken);
}
```

---

## ⚠️ **Important Notes**

### **Azure Durable Functions Compliance**
- ✅ All operations are deterministic
- ✅ External calls only in activities, not orchestrators
- ✅ Proper cancellation token usage
- ✅ JSON serializable models

### **Memory Safety**
- ✅ Processing limited to 25MB chunks
- ✅ Level-by-level processing prevents memory exhaustion
- ✅ Built-in memory monitoring

### **Error Handling**
- ✅ Continue-on-error policy (never stop entire migration)
- ✅ Comprehensive logging with structured data
- ✅ Graceful fallbacks to legacy strategies

### **Performance Optimization**
- ✅ BigCommerce bulk creation API integration
- ✅ Adaptive batch sizing based on performance
- ✅ Minimal HTTP calls (96% reduction)

---

## 📋 **Testing**

### **Unit Tests Coverage**
- **Interface Contracts**: 11 tests covering all interface methods
- **Strategy Implementation**: 15+ tests covering discovery logic
- **Factory Selection**: 8 tests covering strategy routing
- **Activity Functions**: 12+ tests covering Azure Functions activities
- **Error Scenarios**: 10+ tests covering failure handling

### **Integration Tests**
- **End-to-End Migration**: Full category migration workflows
- **Memory Usage Validation**: Performance and memory limit testing
- **BigCommerce API Integration**: Real API interaction testing

---

**Last Updated**: January 2025  
**Version**: 1.0  
**Related Documentation**: 
- [Configuration Guide](Chunked-Migration-Configuration-Guide.md)
- [Migration Guide](Chunked-Migration-Migration-Guide.md)
- [Architecture Documentation](Chunked-Migration-Architecture.md)