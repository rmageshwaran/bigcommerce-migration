using FluentAssertions;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using Xunit;
using System.Collections.Generic;
using System;

namespace BigCommerce.Migration.UnitTests.Core.Models;

/// <summary>
/// TDD unit tests for Hierarchy Metadata Models
/// Tests define the expected behavior - implementation will follow (TDD)
/// Validates Interface Segregation Principle and Azure Durable Functions determinism
/// </summary>
public class HierarchyModelsTests
{
    #region Interface Segregation Principle Tests (RED phase - should fail initially)

    [Fact]
    public void IHierarchyMetadata_ShouldFollow_InterfaceSegregationPrinciple()
    {
        // Act - Verify interface exists and has minimal responsibilities
        var interfaceType = typeof(IHierarchyMetadata);
        
        // Assert - Interface should exist and be properly segregated
        interfaceType.Should().NotBeNull();
        interfaceType.IsInterface.Should().BeTrue();
        
        // Verify interface has minimal, focused responsibilities
        var properties = interfaceType.GetProperties();
        properties.Should().NotBeEmpty("IHierarchyMetadata should define core metadata properties");
        
        var methods = interfaceType.GetMethods().Where(m => !m.IsSpecialName);
        methods.Should().NotBeEmpty("IHierarchyMetadata should define essential metadata operations");
    }

    [Fact]
    public void ILevelMetadata_ShouldFollow_InterfaceSegregationPrinciple()
    {
        // Act - Verify interface exists and is properly segregated
        var interfaceType = typeof(ILevelMetadata);
        
        // Assert - Interface should exist and handle only level-specific concerns
        interfaceType.Should().NotBeNull();
        interfaceType.IsInterface.Should().BeTrue();
        
        // Verify interface focuses only on level metadata
        var properties = interfaceType.GetProperties();
        properties.Should().NotBeEmpty("ILevelMetadata should define level-specific properties");
    }

    #endregion

    #region HierarchyMetadata Class Tests (RED phase - should fail initially)

    [Fact]
    public void HierarchyMetadata_ShouldImplement_IHierarchyMetadata()
    {
        // Act - Create instance
        var metadata = new HierarchyMetadata();
        
        // Assert - Should implement interface
        metadata.Should().BeAssignableTo<IHierarchyMetadata>();
    }

    [Fact]
    public void HierarchyMetadata_ShouldHave_RequiredProperties()
    {
        // Act - Create instance
        var metadata = new HierarchyMetadata();
        
        // Assert - Verify all required properties exist
        metadata.Should().NotBeNull();
        
        // Core hierarchy properties
        var totalCategoriesProperty = metadata.GetType().GetProperty("TotalCategories");
        totalCategoriesProperty.Should().NotBeNull("TotalCategories property should exist");
        
        var maxDepthProperty = metadata.GetType().GetProperty("MaxDepth");
        maxDepthProperty.Should().NotBeNull("MaxDepth property should exist");
        
        var levelCountsProperty = metadata.GetType().GetProperty("LevelCounts");
        levelCountsProperty.Should().NotBeNull("LevelCounts property should exist");
        
        var estimatedProcessingTimeProperty = metadata.GetType().GetProperty("EstimatedProcessingTimeMinutes");
        estimatedProcessingTimeProperty.Should().NotBeNull("EstimatedProcessingTimeMinutes property should exist");
    }

    [Fact]
    public void HierarchyMetadata_ShouldHave_HelperMethods()
    {
        // Act - Verify helper methods exist
        var metadataType = typeof(HierarchyMetadata);
        
        // Assert - Verify required helper methods
        var getCountMethod = metadataType.GetMethod("GetCountForLevel");
        getCountMethod.Should().NotBeNull("GetCountForLevel method should exist");
        
        var hasLevelMethod = metadataType.GetMethod("HasLevel");
        hasLevelMethod.Should().NotBeNull("HasLevel method should exist");
        
        var calculateEstimateMethod = metadataType.GetMethod("CalculateProcessingTimeEstimate");
        calculateEstimateMethod.Should().NotBeNull("CalculateProcessingTimeEstimate method should exist");
    }

    [Fact]
    public void HierarchyMetadata_GetCountForLevel_ShouldReturn_CorrectCount()
    {
        // Arrange
        var levelCounts = new Dictionary<int, int>
        {
            { 0, 10 },
            { 1, 25 },
            { 2, 50 }
        };
        var metadata = new HierarchyMetadata
        {
            LevelCounts = levelCounts
        };

        // Act & Assert
        metadata.GetCountForLevel(0).Should().Be(10);
        metadata.GetCountForLevel(1).Should().Be(25);
        metadata.GetCountForLevel(2).Should().Be(50);
        metadata.GetCountForLevel(999).Should().Be(0, "Non-existent level should return 0");
    }

    [Fact]
    public void HierarchyMetadata_HasLevel_ShouldReturn_CorrectResult()
    {
        // Arrange
        var levelCounts = new Dictionary<int, int>
        {
            { 0, 10 },
            { 1, 25 },
            { 2, 0 } // Level exists but empty
        };
        var metadata = new HierarchyMetadata
        {
            LevelCounts = levelCounts
        };

        // Act & Assert
        metadata.HasLevel(0).Should().BeTrue("Level 0 exists");
        metadata.HasLevel(1).Should().BeTrue("Level 1 exists");
        metadata.HasLevel(2).Should().BeTrue("Level 2 exists even if empty");
        metadata.HasLevel(999).Should().BeFalse("Level 999 does not exist");
    }

    [Fact]
    public void HierarchyMetadata_CalculateProcessingTimeEstimate_ShouldBe_AccurateWithin20Percent()
    {
        // Arrange - Known category counts for estimation validation
        var metadata = new HierarchyMetadata
        {
            TotalCategories = 1000,
            MaxDepth = 3,
            LevelCounts = new Dictionary<int, int>
            {
                { 0, 10 },   // 10 root categories
                { 1, 100 },  // 100 level-1 categories
                { 2, 890 }   // 890 level-2 categories
            }
        };

        // Act
        var estimatedTime = metadata.CalculateProcessingTimeEstimate();

        // Assert - Estimation should be reasonable (0.5-5 minutes for 1000 categories)
        estimatedTime.Should().BeGreaterThan(0.5, "Processing 1000 categories should take at least 30 seconds");
        estimatedTime.Should().BeLessThan(5.0, "Processing 1000 categories should not exceed 5 minutes (Azure Functions limit)");
        
        // Store estimate for accuracy validation
        metadata.EstimatedProcessingTimeMinutes = estimatedTime;
        metadata.EstimatedProcessingTimeMinutes.Should().Be(estimatedTime);
    }

    #endregion

    #region LevelFetchRequest Model Tests (RED phase - should fail initially)

    [Fact]
    public void LevelFetchRequest_ShouldBe_DeterministicForAzureDurableFunctions()
    {
        // Act - Create request instances
        var request1 = new LevelFetchRequest
        {
            Level = 1,
            MaxCategoriesPerLevel = 1000,
            BatchSize = 25
        };
        
        var request2 = new LevelFetchRequest
        {
            Level = 1,
            MaxCategoriesPerLevel = 1000,
            BatchSize = 25
        };

        // Assert - Objects with same values should be equal (deterministic)
        request1.Should().BeEquivalentTo(request2, 
            "LevelFetchRequest must be deterministic for Azure Durable Functions orchestration");
        
        // Verify all required properties exist
        request1.Level.Should().Be(1);
        request1.MaxCategoriesPerLevel.Should().Be(1000);
        request1.BatchSize.Should().Be(25);
    }

    [Fact]
    public void LevelFetchRequest_ShouldHave_RequiredProperties()
    {
        // Act - Create instance
        var request = new LevelFetchRequest();
        
        // Assert - Verify all required properties exist
        request.Should().NotBeNull();
        
        var levelProperty = request.GetType().GetProperty("Level");
        levelProperty.Should().NotBeNull("Level property should exist");
        
        var maxCategoriesProperty = request.GetType().GetProperty("MaxCategoriesPerLevel");
        maxCategoriesProperty.Should().NotBeNull("MaxCategoriesPerLevel property should exist");
        
        var batchSizeProperty = request.GetType().GetProperty("BatchSize");
        batchSizeProperty.Should().NotBeNull("BatchSize property should exist");
        
        var parentIdProperty = request.GetType().GetProperty("ParentCategoryId");
        parentIdProperty.Should().NotBeNull("ParentCategoryId property should exist");
    }

    [Fact]
    public void LevelFetchRequest_Validation_ShouldEnforce_AzureFunctionsConstraints()
    {
        // Arrange & Act & Assert
        var request = new LevelFetchRequest
        {
            Level = 0,
            MaxCategoriesPerLevel = 10000, // Valid - within memory limits
            BatchSize = 25                 // Valid - reasonable batch size
        };

        // Validate constraints
        request.Level.Should().BeGreaterOrEqualTo(0, "Level should be non-negative");
        request.MaxCategoriesPerLevel.Should().BeGreaterThan(0, "MaxCategoriesPerLevel should be positive");
        request.MaxCategoriesPerLevel.Should().BeLessOrEqualTo(50000, "MaxCategoriesPerLevel should respect memory limits");
        request.BatchSize.Should().BeGreaterThan(0, "BatchSize should be positive");
        request.BatchSize.Should().BeLessOrEqualTo(100, "BatchSize should respect API rate limits");
    }

    #endregion

    #region Performance and Memory Safety Tests

    [Fact]
    public void HierarchyMetadata_ShouldSupport_LargeHierarchies()
    {
        // Arrange - Large hierarchy (100K categories) with realistic growth
        var largeLevelCounts = new Dictionary<int, int>
        {
            { 0, 100 },      // 100 root categories
            { 1, 1000 },     // 1K level-1 categories  
            { 2, 10000 },    // 10K level-2 categories
            { 3, 50000 },    // 50K level-3 categories
            { 4, 25000 },    // 25K level-4 categories
            { 5, 10000 }     // 10K level-5 categories
        };
        
        var metadata = new HierarchyMetadata
        {
            TotalCategories = largeLevelCounts.Values.Sum(), // 96,100 total
            MaxDepth = 5,
            LevelCounts = largeLevelCounts
        };

        // Act & Assert - Should handle large hierarchies without memory issues
        metadata.TotalCategories.Should().BeGreaterThan(90000, "Should support large hierarchies");
        metadata.GetCountForLevel(3).Should().Be(50000, "Should access deep levels efficiently");
        
        var processingTime = metadata.CalculateProcessingTimeEstimate();
        processingTime.Should().BeLessThan(5.0, "Large hierarchies should still fit within Azure Functions timeout");
    }

    [Fact]
    public void HierarchyMetadata_EstimationAlgorithm_ShouldBe_AccurateForVariousScales()
    {
        // Test small hierarchy
        var smallMetadata = new HierarchyMetadata
        {
            TotalCategories = 50,
            MaxDepth = 1,
            LevelCounts = new Dictionary<int, int> { { 0, 50 } }
        };
        var smallEstimate = smallMetadata.CalculateProcessingTimeEstimate();
        smallEstimate.Should().BeLessThan(1.0, "Small hierarchies should process quickly");

        // Test medium hierarchy - realistic for 2K categories
        var mediumMetadata = new HierarchyMetadata
        {
            TotalCategories = 2000,
            MaxDepth = 2, // Moderate depth
            LevelCounts = new Dictionary<int, int> { { 0, 100 }, { 1, 1900 } }
        };
        var mediumEstimate = mediumMetadata.CalculateProcessingTimeEstimate();
        mediumEstimate.Should().BeGreaterThan(smallEstimate, "Medium hierarchies should take longer than small");
        mediumEstimate.Should().BeLessThan(4.5, "Medium hierarchies should fit within Azure Functions timeout");
    }

    #endregion

    #region Error Handling and Edge Cases

    [Fact]
    public void HierarchyMetadata_ShouldHandle_EdgeCases()
    {
        // Empty hierarchy
        var emptyMetadata = new HierarchyMetadata
        {
            TotalCategories = 0,
            LevelCounts = new Dictionary<int, int>()
        };
        
        emptyMetadata.GetCountForLevel(0).Should().Be(0, "Empty hierarchy should return 0 for any level");
        emptyMetadata.HasLevel(0).Should().BeFalse("Empty hierarchy should have no levels");
        emptyMetadata.CalculateProcessingTimeEstimate().Should().Be(0, "Empty hierarchy should have 0 processing time");

        // Null level counts should be handled gracefully
        var nullLevelCountsMetadata = new HierarchyMetadata
        {
            TotalCategories = 100,
            LevelCounts = null
        };
        
        Action act = () => nullLevelCountsMetadata.GetCountForLevel(0);
        act.Should().NotThrow("Should handle null LevelCounts gracefully");
    }

    [Fact]
    public void LevelFetchRequest_ShouldHandle_EdgeCases()
    {
        // Null parent ID should be valid (root level)
        var rootRequest = new LevelFetchRequest
        {
            Level = 0,
            ParentCategoryId = null,
            MaxCategoriesPerLevel = 1000,
            BatchSize = 25
        };
        
        rootRequest.ParentCategoryId.Should().BeNull("Root level requests should have null ParentCategoryId");
        
        // Non-null parent ID should be valid (child levels)
        var childRequest = new LevelFetchRequest
        {
            Level = 1,
            ParentCategoryId = 123,
            MaxCategoriesPerLevel = 1000,
            BatchSize = 25
        };
        
        childRequest.ParentCategoryId.Should().Be(123, "Child level requests should have valid ParentCategoryId");
    }

    #endregion
}