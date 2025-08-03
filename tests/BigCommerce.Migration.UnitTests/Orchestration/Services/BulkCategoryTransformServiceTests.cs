using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Services;
using BigCommerce.Migration.Orchestration.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System;
using System.Linq;

namespace BigCommerce.Migration.UnitTests.Orchestration.Services;

/// <summary>
/// TDD unit tests for BulkCategoryTransformService
/// Tests define the expected behavior for bulk category transformation with hierarchical relationships
/// Focuses on leveraging existing CategoryTransformStrategy in bulk-ready batches
/// </summary>
public class BulkCategoryTransformServiceTests
{
    private readonly Mock<ILogger<BulkCategoryTransformService>> _mockLogger;
    private readonly Mock<IEntityTransformStrategy> _mockCategoryTransformStrategy;
    private readonly Mock<IEntityMappingService> _mockEntityMappingService;
    private readonly BatchProcessingRequest _testBatchRequest;
    private readonly CategoryTreeContext _testCategoryTreeContext;

    public BulkCategoryTransformServiceTests()
    {
        _mockLogger = new Mock<ILogger<BulkCategoryTransformService>>();
        _mockCategoryTransformStrategy = new Mock<IEntityTransformStrategy>();
        _mockEntityMappingService = new Mock<IEntityMappingService>();

        _testCategoryTreeContext = new CategoryTreeContext
        {
            SourceCategoryTreeId = "source-tree-123",
            DestinationCategoryTreeId = "dest-tree-456",
            SourceChannelId = "source-channel-789",
            DestinationChannelId = "dest-channel-012"
        };

        _testBatchRequest = new BatchProcessingRequest
        {
            MigrationId = "test-migration-bulk-transform",
            EntityType = "categories",
            SourceStore = new StoreConfiguration
            {
                StoreId = "source-store-123",
                AccessToken = "source-token",
                ChannelId = "source-channel-789"
            },
            DestinationStore = new StoreConfiguration
            {
                StoreId = "dest-store-456",
                AccessToken = "dest-token", 
                ChannelId = "dest-channel-012"
            },
            CategoryTreeContext = _testCategoryTreeContext
        };

        _mockCategoryTransformStrategy.Setup(x => x.EntityType).Returns("categories");
    }

    #region Service Structure Tests (TDD)

    [Fact]
    public void BulkCategoryTransformService_ShouldHave_ProperConstructorDependencies()
    {
        // Arrange & Act
        var constructors = typeof(BulkCategoryTransformService).GetConstructors();
        
        // Assert - Verify dependency injection structure
        constructors.Should().HaveCount(1, "Should have single constructor for DI");
        
        var constructor = constructors[0];
        var parameters = constructor.GetParameters();
        
        parameters.Should().Contain(p => p.ParameterType == typeof(IEntityTransformStrategy),
            "Should depend on existing CategoryTransformStrategy");
        parameters.Should().Contain(p => p.ParameterType == typeof(ILogger<BulkCategoryTransformService>),
            "Should depend on logger for bulk transformation observability");
    }

    [Fact]
    public async Task TransformCategoriesInBulkAsync_ShouldAccept_ProperInputParameters()
    {
        // Arrange
        var service = CreateService();
        var categories = CreateTestHierarchicalCategories();

        // Act & Assert - Should not throw for valid input
        var result = await service.TransformCategoriesInBulkAsync(
            categories, 
            _testBatchRequest, 
            CancellationToken.None);
            
        result.Should().NotBeNull("Should return valid result for proper input");
    }

    #endregion

    #region Bulk Transformation Core Tests (Task 3.2.2)

    [Fact]
    public async Task TransformCategoriesInBulkAsync_ShouldTransform_CategoriesInHierarchicalOrder()
    {
        // Arrange
        var service = CreateService();
        var hierarchicalCategories = CreateTestHierarchicalCategories();

        SetupCategoryTransformStrategy();

        // Act
        var result = await service.TransformCategoriesInBulkAsync(
            hierarchicalCategories, 
            _testBatchRequest, 
            CancellationToken.None);

        // Assert - Hierarchical order processing
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Should successfully transform hierarchical categories");
        result.TransformedCategories.Should().HaveCount(5, "Should transform all 5 categories");
        result.TotalCategories.Should().Be(5, "Should track total category count");
        result.ProcessedCategories.Should().Be(5, "Should process all categories");
        result.FailedCategories.Should().Be(0, "Should have no failures for valid hierarchical data");

        // Verify categories are transformed in correct hierarchical order (roots first)
        var transformedNames = result.TransformedCategories.Select(c => c.GetValueOrDefault("name")?.ToString()).ToList();
        transformedNames[0].Should().Be("Electronics", "Root category should be processed first");
        
        // Verify transformation strategy called for each category
        _mockCategoryTransformStrategy.Verify(x => x.TransformEntityAsync(
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<string>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<CategoryTreeContext>(),
            It.IsAny<CancellationToken>()), 
            Times.Exactly(5), "Should call transform strategy for each category");
    }

    [Fact]
    public async Task TransformCategoriesInBulkAsync_ShouldApply_ParentIdMappingInBatches()
    {
        // Arrange
        var service = CreateService();
        var hierarchicalCategories = CreateTestHierarchicalCategories();

        SetupCategoryTransformStrategy();
        SetupParentIdMapping();

        // Act
        var result = await service.TransformCategoriesInBulkAsync(
            hierarchicalCategories, 
            _testBatchRequest, 
            CancellationToken.None);

        // Assert - Parent ID mapping in batches
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Should successfully apply parent ID mapping");
        result.ParentIdMappingsApplied.Should().Be(4, "Should apply parent ID mapping for 4 child categories (excluding root)");
        
        // Verify parent ID mappings are updated
        var parentCategories = result.TransformedCategories
            .Where(c => c.ContainsKey("parent_id") && !c["parent_id"]?.ToString()?.Equals("0") == true)
            .ToList();
        parentCategories.Should().HaveCount(4, "Should have 4 categories with parent relationships");

        // Verify existing transformation strategy is leveraged
        _mockCategoryTransformStrategy.Verify(x => x.TransformEntityAsync(
            It.Is<Dictionary<string, object>>(cat => cat.ContainsKey("parent_id")),
            It.IsAny<string>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<CategoryTreeContext>(),
            It.IsAny<CancellationToken>()), 
            Times.AtLeast(4), "Should use existing transform strategy for parent ID mapping");
    }

    [Fact]
    public async Task TransformCategoriesInBulkAsync_ShouldApply_TreeIdAssignmentInBulk()
    {
        // Arrange
        var service = CreateService();
        var categories = CreateTestHierarchicalCategories();

        SetupCategoryTransformStrategy();

        // Act
        var result = await service.TransformCategoriesInBulkAsync(
            categories, 
            _testBatchRequest, 
            CancellationToken.None);

        // Assert - Tree ID assignment in bulk
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Should successfully assign tree IDs in bulk");
        result.TreeIdAssignmentsApplied.Should().Be(5, "Should apply tree ID assignment for all 5 categories");
        
        // Verify tree ID assignment consistency
        var allCategoriesHaveTreeId = result.TransformedCategories.All(c => 
            c.ContainsKey("category_tree_id") && 
            c["category_tree_id"]?.ToString() == _testCategoryTreeContext.DestinationCategoryTreeId);
        allCategoriesHaveTreeId.Should().BeTrue("All categories should have consistent destination tree ID");

        // Verify CategoryTreeContext is passed to transformation strategy
        _mockCategoryTransformStrategy.Verify(x => x.TransformEntityAsync(
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<string>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<StoreConfiguration>(),
            It.Is<CategoryTreeContext>(ctx => ctx.DestinationCategoryTreeId == _testCategoryTreeContext.DestinationCategoryTreeId),
            It.IsAny<CancellationToken>()), 
            Times.Exactly(5), "Should pass CategoryTreeContext to existing transform strategy");
    }

    [Fact]
    public async Task TransformCategoriesInBulkAsync_ShouldProcess_CategoriesInConfigurableBatches()
    {
        // Arrange
        var service = CreateService();
        var largeCategorySet = CreateLargeCategoryHierarchy(100); // 100 categories

        SetupCategoryTransformStrategy();

        // Act
        var result = await service.TransformCategoriesInBulkAsync(
            largeCategorySet, 
            _testBatchRequest, 
            CancellationToken.None,
            batchSize: 25); // Process in batches of 25

        // Assert - Configurable batch processing
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Should successfully process large hierarchy in batches");
        result.TotalCategories.Should().Be(100, "Should track total category count");
        result.ProcessedCategories.Should().Be(100, "Should process all categories");
        result.BatchesProcessed.Should().Be(4, "Should process 100 categories in 4 batches of 25");
        result.ProcessingTimeMinutes.Should().BeGreaterThan(0, "Should track processing time");
        result.MemoryUsageMB.Should().BeGreaterThan(0, "Should track memory usage");

        // Verify batch processing doesn't break hierarchical order
        var transformedNames = result.TransformedCategories.Take(10).Select(c => c.GetValueOrDefault("name")?.ToString()).ToList();
        transformedNames.Should().Contain("Root Category 0", "Root categories should be processed first in batches");
    }

    [Fact]
    public async Task TransformCategoriesInBulkAsync_ShouldHandle_PartialTransformationFailures()
    {
        // Arrange
        var service = CreateService();
        var categories = CreateTestHierarchicalCategories();

        // Setup transformation strategy to fail on specific category
        _mockCategoryTransformStrategy.Setup(x => x.TransformEntityAsync(
            It.Is<Dictionary<string, object>>(c => c.ContainsKey("name") && c["name"].ToString() == "Computers"),
            It.IsAny<string>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<CategoryTreeContext>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated transformation failure"));

        // Setup successful transformations for other categories
        _mockCategoryTransformStrategy.Setup(x => x.TransformEntityAsync(
            It.Is<Dictionary<string, object>>(c => !c.ContainsKey("name") || c["name"].ToString() != "Computers"),
            It.IsAny<string>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<CategoryTreeContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, object> entity, string migrationId, 
                StoreConfiguration source, StoreConfiguration dest, 
                CategoryTreeContext ctx, CancellationToken ct) => 
                {
                    var transformed = new Dictionary<string, object>(entity);
                    transformed["category_tree_id"] = ctx?.DestinationCategoryTreeId ?? "default-tree";
                    return transformed;
                });

        // Act
        var result = await service.TransformCategoriesInBulkAsync(
            categories, 
            _testBatchRequest, 
            CancellationToken.None);

        // Assert - Partial failure handling with continue-on-error
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Should succeed with continue-on-error policy despite partial failures");
        result.ProcessedCategories.Should().Be(4, "Should process 4 successful categories");
        result.FailedCategories.Should().Be(1, "Should track 1 failed category");
        result.TransformationErrors.Should().HaveCount(1, "Should capture 1 transformation error");
        result.TransformationErrors[0].Should().Contain("Computers", "Should identify which category failed");
        result.SuccessRatePercent.Should().Be(80.0, "Should calculate 80% success rate (4/5)");
    }

    [Fact]
    public async Task TransformCategoriesInBulkAsync_ShouldOptimize_MemoryUsageForLargeHierarchies()
    {
        // Arrange
        var service = CreateService();
        var largeHierarchy = CreateLargeCategoryHierarchy(500); // Memory-intensive load

        SetupCategoryTransformStrategy();

        // Act
        var result = await service.TransformCategoriesInBulkAsync(
            largeHierarchy, 
            _testBatchRequest, 
            CancellationToken.None,
            batchSize: 50);

        // Assert - Memory optimization for large hierarchies
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Should handle large hierarchy with memory optimization");
        result.TotalCategories.Should().Be(500, "Should process all 500 categories");
        result.MemoryUsageMB.Should().BeLessOrEqualTo(50.0, "Should stay within memory limits for large hierarchies");
        result.ProcessingTimeMinutes.Should().BeLessOrEqualTo(5.0, "Should complete within reasonable time");
        result.BatchesProcessed.Should().Be(10, "Should process 500 categories in 10 batches of 50");

        // Verify memory optimization doesn't compromise transformation quality
        var allHaveTreeId = result.TransformedCategories.All(c => c.ContainsKey("category_tree_id"));
        allHaveTreeId.Should().BeTrue("Memory optimization should not compromise transformation completeness");
    }

    [Fact]
    public async Task TransformCategoriesInBulkAsync_ShouldProvide_DetailedTransformationMetrics()
    {
        // Arrange
        var service = CreateService();
        var categories = CreateTestHierarchicalCategories();

        SetupCategoryTransformStrategy();

        // Act
        var result = await service.TransformCategoriesInBulkAsync(
            categories, 
            _testBatchRequest, 
            CancellationToken.None);

        // Assert - Detailed transformation metrics
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Should provide detailed metrics");
        
        // Verify comprehensive metrics
        result.ProcessingTimeMinutes.Should().BeGreaterThan(0, "Should track processing time");
        result.MemoryUsageMB.Should().BeGreaterThan(0, "Should track memory usage");
        result.ThroughputCategoriesPerMinute.Should().BeGreaterThan(0, "Should calculate throughput");
        result.AverageTransformationTimeMs.Should().BeGreaterThan(0, "Should track average transformation time");
        
        // Verify summary information
        var summary = result.GetSummary();
        summary.Should().Contain("Bulk Category Transformation Summary");
        summary.Should().Contain("5/5 (100.0%)");
        summary.Should().Contain("Tree ID Assignments: 5");
        summary.Should().Contain("Parent ID Mappings: 4");

        var resultString = result.ToString();
        resultString.Should().Contain("BulkTransformResult");
        resultString.Should().Contain("Success=True");
        resultString.Should().Contain("5/5");
    }

    #endregion

    #region Helper Methods

    private BulkCategoryTransformService CreateService()
    {
        return new BulkCategoryTransformService(
            _mockCategoryTransformStrategy.Object,
            _mockLogger.Object);
    }

    private void SetupCategoryTransformStrategy()
    {
        _mockCategoryTransformStrategy.Setup(x => x.TransformEntityAsync(
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<string>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<CategoryTreeContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, object> entity, string migrationId, 
                StoreConfiguration source, StoreConfiguration dest, 
                CategoryTreeContext ctx, CancellationToken ct) => 
                {
                    var transformed = new Dictionary<string, object>(entity);
                    transformed["category_tree_id"] = ctx?.DestinationCategoryTreeId ?? "default-tree";
                    return transformed;
                });
    }

    private void SetupParentIdMapping()
    {
        // Mock parent ID mappings for hierarchical relationships
        _mockEntityMappingService.Setup(x => x.GetDestinationIdAsync(
            It.IsAny<string>(),
            "categories",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string migrationId, string entityType, string sourceId, CancellationToken ct) =>
            {
                // Simulate mapped parent IDs
                return sourceId switch
                {
                    "1" => "1001", // Electronics -> 1001
                    "2" => "1002", // Computers -> 1002
                    _ => null
                };
            });
    }

    private List<Dictionary<string, object>> CreateTestHierarchicalCategories()
    {
        return new List<Dictionary<string, object>>
        {
            // Root category
            new Dictionary<string, object>
            {
                {"id", "1"},
                {"name", "Electronics"},
                {"parent_id", 0},
                {"description", "Root electronics category"}
            },
            // Level 1 categories
            new Dictionary<string, object>
            {
                {"id", "2"},
                {"name", "Computers"},
                {"parent_id", "1"},
                {"description", "Computer products"}
            },
            new Dictionary<string, object>
            {
                {"id", "3"},
                {"name", "Mobile Phones"},
                {"parent_id", "1"},
                {"description", "Mobile phone products"}
            },
            // Level 2 categories
            new Dictionary<string, object>
            {
                {"id", "4"},
                {"name", "Laptops"},
                {"parent_id", "2"},
                {"description", "Laptop computers"}
            },
            new Dictionary<string, object>
            {
                {"id", "5"},
                {"name", "Gaming Phones"},
                {"parent_id", "3"},
                {"description", "Gaming-focused mobile phones"}
            }
        };
    }

    private List<Dictionary<string, object>> CreateLargeCategoryHierarchy(int totalCategories)
    {
        var categories = new List<Dictionary<string, object>>();
        
        // Create root categories (every 10th category)
        for (int i = 0; i < totalCategories; i += 10)
        {
            categories.Add(new Dictionary<string, object>
            {
                {"id", i.ToString()},
                {"name", $"Root Category {i}"},
                {"parent_id", 0},
                {"description", $"Root category {i}"}
            });
        }

        // Create child categories
        for (int i = 1; i < totalCategories; i++)
        {
            if (i % 10 != 0) // Skip root categories
            {
                var parentId = (i / 10) * 10; // Parent is the nearest root
                categories.Add(new Dictionary<string, object>
                {
                    {"id", i.ToString()},
                    {"name", $"Child Category {i}"},
                    {"parent_id", parentId.ToString()},
                    {"description", $"Child category {i}"}
                });
            }
        }

        return categories;
    }

    #endregion
}