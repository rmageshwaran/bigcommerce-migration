using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Service for transforming categories in bulk-ready batches while preserving hierarchical relationships
/// Leverages existing CategoryTransformStrategy for individual transformations
/// Implements Task 3.2.2: Category transformation in bulk-ready batches
/// </summary>
public class BulkCategoryTransformService
{
    private readonly IEntityTransformStrategy _categoryTransformStrategy;
    private readonly ILogger<BulkCategoryTransformService> _logger;

    /// <summary>
    /// Initializes a new instance of BulkCategoryTransformService
    /// </summary>
    /// <param name="categoryTransformStrategy">Existing category transformation strategy</param>
    /// <param name="logger">Logger instance for bulk transformation observability</param>
    public BulkCategoryTransformService(
        IEntityTransformStrategy categoryTransformStrategy,
        ILogger<BulkCategoryTransformService> logger)
    {
        _categoryTransformStrategy = categoryTransformStrategy ?? throw new ArgumentNullException(nameof(categoryTransformStrategy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Transforms categories in bulk-ready batches while preserving hierarchical relationships
    /// Leverages existing CategoryTransformStrategy for individual category transformations
    /// </summary>
    /// <param name="categories">Categories to transform in hierarchical order</param>
    /// <param name="batchRequest">Batch processing request with migration context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="batchSize">Optional batch size for processing (default: 25)</param>
    /// <returns>Bulk transformation result with comprehensive metrics</returns>
    public async Task<BulkCategoryTransformationResult> TransformCategoriesInBulkAsync(
        List<Dictionary<string, object>> categories,
        BatchProcessingRequest batchRequest,
        CancellationToken cancellationToken,
        int batchSize = 25)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new BulkCategoryTransformationResult
        {
            TotalCategories = categories.Count,
            Success = false
        };

        _logger.LogInformation("🔄 Starting bulk category transformation for Migration {MigrationId}: {CategoryCount} categories in batches of {BatchSize}", 
            batchRequest.MigrationId, categories.Count, batchSize);

        try
        {
            // 1. Sort categories in hierarchical order (roots first)
            var hierarchicalCategories = SortCategoriesHierarchically(categories);

            // 2. Process categories in configurable batches
            var batches = CreateBatches(hierarchicalCategories, batchSize);
            result.BatchesProcessed = batches.Count;

            _logger.LogInformation("📦 Processing {CategoryCount} categories in {BatchCount} batches for Migration {MigrationId}", 
                categories.Count, batches.Count, batchRequest.MigrationId);

            // 3. Transform each batch
            var batchNumber = 1;
            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                await TransformBatch(batch, batchNumber, batchRequest, result, cancellationToken);
                
                // Monitor memory usage during batch processing
                MonitorMemoryUsage(result, batchNumber);
                
                batchNumber++;
            }

            // 4. Calculate final metrics
            stopwatch.Stop();
            result.ProcessingTimeMinutes = stopwatch.Elapsed.TotalMinutes;
            result.MemoryUsageMB = GetMemoryUsageMB();

            // 5. Determine success based on continue-on-error policy
            result.Success = result.ProcessedCategories > 0 || result.TotalCategories == 0;

            _logger.LogInformation("✅ Completed bulk category transformation for Migration {MigrationId}: " +
                "{ProcessedCategories}/{TotalCategories} processed, {TreeIdAssignments} tree ID assignments, " +
                "{ParentIdMappings} parent ID mappings, {ProcessingTimeMinutes:F2}min, {MemoryUsageMB:F2}MB",
                batchRequest.MigrationId, result.ProcessedCategories, result.TotalCategories, 
                result.TreeIdAssignmentsApplied, result.ParentIdMappingsApplied, 
                result.ProcessingTimeMinutes, result.MemoryUsageMB);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("🚫 Bulk category transformation for Migration {MigrationId} was cancelled", 
                batchRequest.MigrationId);
            
            stopwatch.Stop();
            result.ProcessingTimeMinutes = stopwatch.Elapsed.TotalMinutes;
            result.Success = false;
            result.TransformationErrors.Add("Bulk transformation cancelled");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ An error occurred during bulk category transformation for Migration {MigrationId}: {ErrorMessage}", 
                batchRequest.MigrationId, ex.Message);
            
            stopwatch.Stop();
            result.ProcessingTimeMinutes = stopwatch.Elapsed.TotalMinutes;
            result.Success = false;
            result.TransformationErrors.Add($"Bulk transformation error: {ex.Message}");
            return result;
        }
    }

    #region Batch Processing Methods

    /// <summary>
    /// Transforms a single batch of categories using existing CategoryTransformStrategy
    /// </summary>
    private async Task TransformBatch(
        List<Dictionary<string, object>> batch, 
        int batchNumber, 
        BatchProcessingRequest batchRequest, 
        BulkCategoryTransformationResult result, 
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("🔄 Processing batch {BatchNumber} with {CategoryCount} categories", batchNumber, batch.Count);

        foreach (var category in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                // Use existing CategoryTransformStrategy for individual category transformation
                var transformedCategory = await _categoryTransformStrategy.TransformEntityAsync(
                    category,
                    batchRequest.MigrationId,
                    batchRequest.SourceStore,
                    batchRequest.DestinationStore,
                    batchRequest.CategoryTreeContext,
                    cancellationToken);

                // Track successful transformation
                result.TransformedCategories.Add(transformedCategory);
                result.ProcessedCategories++;

                // Track tree ID assignments
                if (transformedCategory.ContainsKey("category_tree_id"))
                {
                    result.TreeIdAssignmentsApplied++;
                }

                // Track parent ID mappings (categories with non-zero parent_id)
                if (transformedCategory.TryGetValue("parent_id", out var parentId) && 
                    parentId != null && !parentId.ToString()?.Equals("0") == true)
                {
                    result.ParentIdMappingsApplied++;
                }

                // Store category mapping for tracking
                var categoryId = category.GetValueOrDefault("id")?.ToString() ?? "unknown";
                result.CategoryMappings[categoryId] = transformedCategory;

                _logger.LogDebug("✅ Successfully transformed category {CategoryId} in batch {BatchNumber}", 
                    categoryId, batchNumber);
            }
            catch (Exception ex)
            {
                var categoryId = category.GetValueOrDefault("id")?.ToString() ?? "unknown";
                var categoryName = category.GetValueOrDefault("name")?.ToString() ?? "unknown";
                
                _logger.LogWarning(ex, "⚠️ Failed to transform category {CategoryId} ('{CategoryName}') in batch {BatchNumber}: {ErrorMessage}", 
                    categoryId, categoryName, batchNumber, ex.Message);
                
                result.FailedCategories++;
                result.TransformationErrors.Add($"Category {categoryId} ({categoryName}): {ex.Message}");
                
                // Continue-on-error policy: continue processing other categories
            }
        }

        _logger.LogDebug("📋 Completed batch {BatchNumber}: {SuccessCount} successful, {FailureCount} failed", 
            batchNumber, batch.Count - result.TransformationErrors.Count, result.TransformationErrors.Count);
    }

    /// <summary>
    /// Sorts categories in hierarchical order (roots first, then children)
    /// </summary>
    private List<Dictionary<string, object>> SortCategoriesHierarchically(List<Dictionary<string, object>> categories)
    {
        var sorted = new List<Dictionary<string, object>>();
        var processed = new HashSet<string>();
        
        _logger.LogDebug("🏗️ Sorting {CategoryCount} categories in hierarchical order", categories.Count);

        // Function to recursively add categories in hierarchical order
        void AddCategoryAndChildren(Dictionary<string, object> category)
        {
            var categoryId = category.GetValueOrDefault("id")?.ToString() ?? "";
            if (processed.Contains(categoryId)) return;

            sorted.Add(category);
            processed.Add(categoryId);

            // Find and add children
            var children = categories.Where(c => 
            {
                var parentId = c.GetValueOrDefault("parent_id")?.ToString();
                return parentId == categoryId;
            }).ToList();

            foreach (var child in children)
            {
                AddCategoryAndChildren(child);
            }
        }

        // Start with root categories (parent_id = 0, null, or empty)
        var rootCategories = categories.Where(c => 
        {
            var parentId = c.GetValueOrDefault("parent_id")?.ToString();
            return string.IsNullOrEmpty(parentId) || parentId == "0" || parentId == "null";
        }).ToList();

        foreach (var root in rootCategories)
        {
            AddCategoryAndChildren(root);
        }

        // Add any remaining categories (in case of orphaned or missing parent references)
        foreach (var category in categories)
        {
            var categoryId = category.GetValueOrDefault("id")?.ToString() ?? "";
            if (!processed.Contains(categoryId))
            {
                sorted.Add(category);
                processed.Add(categoryId);
            }
        }

        _logger.LogDebug("✅ Sorted categories: {RootCount} roots found, {TotalSorted} total sorted", 
            rootCategories.Count, sorted.Count);

        return sorted;
    }

    /// <summary>
    /// Creates batches of categories for bulk processing
    /// </summary>
    private List<List<Dictionary<string, object>>> CreateBatches(
        List<Dictionary<string, object>> categories, 
        int batchSize)
    {
        var batches = new List<List<Dictionary<string, object>>>();
        
        for (int i = 0; i < categories.Count; i += batchSize)
        {
            var batch = categories.Skip(i).Take(batchSize).ToList();
            batches.Add(batch);
        }
        
        return batches;
    }

    /// <summary>
    /// Monitors memory usage during batch processing
    /// </summary>
    private void MonitorMemoryUsage(BulkCategoryTransformationResult result, int batchNumber)
    {
        var currentMemoryMB = GetMemoryUsageMB();
        
        // Track memory usage
        result.MemoryUsageMB = Math.Max(result.MemoryUsageMB, currentMemoryMB);
        
        // Memory optimization for large hierarchies
        if (result.TotalCategories >= 500)
        {
            // Force garbage collection every 5 batches for large hierarchies
            if (batchNumber % 5 == 0)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                var afterGcMemoryMB = GetMemoryUsageMB();
                _logger.LogDebug("🧹 Memory cleanup after batch {BatchNumber}: {BeforeGC:F1}MB -> {AfterGC:F1}MB", 
                    batchNumber, currentMemoryMB, afterGcMemoryMB);
            }
        }
    }

    /// <summary>
    /// Gets current memory usage in MB
    /// </summary>
    private double GetMemoryUsageMB()
    {
        var memoryBytes = GC.GetTotalMemory(false);
        return memoryBytes / (1024.0 * 1024.0);
    }

    #endregion
}