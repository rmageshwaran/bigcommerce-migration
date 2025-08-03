using FluentAssertions;
using BigCommerce.Migration.Core.Models;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Core.Models;

/// <summary>
/// TDD unit tests for ChunkedHierarchyConfiguration
/// Tests define the expected behavior - implementation will follow (TDD)
/// Validates Single Responsibility Principle by testing configuration validation only
/// </summary>
public class ChunkedHierarchyConfigurationTests
{
    #region Constructor and Property Tests (RED phase - should fail initially)

    [Fact]
    public void ChunkedHierarchyConfiguration_ShouldHave_RequiredProperties()
    {
        // Act - Create instance with default constructor
        var config = new ChunkedHierarchyConfiguration();
        
        // Assert - Verify all required properties exist
        config.Should().NotBeNull();
        config.MaxCategoriesPerLevel.Should().BeGreaterThan(0);
        config.BatchSizePerLevel.Should().BeGreaterThan(0);
        config.MaxBulkCreateSize.Should().BeGreaterThan(0);
        config.MaxHierarchyDepth.Should().BeGreaterThan(0);
        config.FallbackThreshold.Should().BeGreaterThan(0);
        config.EnableMemoryMonitoring.Should().BeTrue(); // Default to enabled
        config.LevelProcessingTimeoutMinutes.Should().BeGreaterThan(0);
        config.BulkCreationTimeoutMinutes.Should().BeGreaterThan(0);
        config.EnableBulkCreation.Should().BeTrue(); // Default to enabled for performance
        config.AdaptiveBatchSizing.Should().BeTrue(); // Default to enabled
    }

    [Fact]
    public void ChunkedHierarchyConfiguration_ShouldHave_ProductionReadyDefaults()
    {
        // Act
        var config = new ChunkedHierarchyConfiguration();
        
        // Assert - Verify production-ready default values
        config.MaxCategoriesPerLevel.Should().Be(10000); // Support large hierarchies
        config.BatchSizePerLevel.Should().Be(25); // Optimal batch size for BigCommerce API
        config.MaxBulkCreateSize.Should().Be(50); // Maximum bulk creation size
        config.MaxHierarchyDepth.Should().Be(10); // Reasonable hierarchy depth limit
        config.FallbackThreshold.Should().Be(25000); // When to fallback to legacy processing
        config.LevelProcessingTimeoutMinutes.Should().Be(4); // Within Azure Functions 5-minute limit
        config.BulkCreationTimeoutMinutes.Should().Be(2); // Reasonable timeout for bulk operations
    }

    #endregion

    #region Validation Tests (RED phase)

    [Fact]
    public void Validate_WithValidConfiguration_ShouldReturnTrue()
    {
        // Arrange
        var config = new ChunkedHierarchyConfiguration
        {
            MaxCategoriesPerLevel = 10000,
            BatchSizePerLevel = 25,
            MaxBulkCreateSize = 50,
            MaxHierarchyDepth = 10,
            FallbackThreshold = 25000,
            EnableMemoryMonitoring = true,
            LevelProcessingTimeoutMinutes = 4,
            BulkCreationTimeoutMinutes = 2,
            EnableBulkCreation = true,
            AdaptiveBatchSizing = true
        };

        // Act
        var result = config.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
        result.ValidationErrors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, "MaxCategoriesPerLevel must be greater than 0")]
    [InlineData(-1, "MaxCategoriesPerLevel must be greater than 0")]
    public void Validate_WithInvalidMaxCategoriesPerLevel_ShouldReturnFalse(int invalidValue, string expectedError)
    {
        // Arrange
        var config = new ChunkedHierarchyConfiguration { MaxCategoriesPerLevel = invalidValue };

        // Act
        var result = config.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(expectedError);
    }

    [Theory]
    [InlineData(0, "BatchSizePerLevel must be between 1 and 100")]
    [InlineData(-1, "BatchSizePerLevel must be between 1 and 100")]
    [InlineData(101, "BatchSizePerLevel must be between 1 and 100")]
    public void Validate_WithInvalidBatchSizePerLevel_ShouldReturnFalse(int invalidValue, string expectedError)
    {
        // Arrange
        var config = new ChunkedHierarchyConfiguration { BatchSizePerLevel = invalidValue };

        // Act
        var result = config.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(expectedError);
    }

    [Theory]
    [InlineData(0, "MaxBulkCreateSize must be between 1 and 100")]
    [InlineData(-1, "MaxBulkCreateSize must be between 1 and 100")]
    [InlineData(101, "MaxBulkCreateSize must be between 1 and 100")]
    public void Validate_WithInvalidMaxBulkCreateSize_ShouldReturnFalse(int invalidValue, string expectedError)
    {
        // Arrange
        var config = new ChunkedHierarchyConfiguration { MaxBulkCreateSize = invalidValue };

        // Act
        var result = config.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(expectedError);
    }

    [Theory]
    [InlineData(0, "MaxHierarchyDepth must be between 1 and 20")]
    [InlineData(-1, "MaxHierarchyDepth must be between 1 and 20")]
    [InlineData(21, "MaxHierarchyDepth must be between 1 and 20")]
    public void Validate_WithInvalidMaxHierarchyDepth_ShouldReturnFalse(int invalidValue, string expectedError)
    {
        // Arrange
        var config = new ChunkedHierarchyConfiguration { MaxHierarchyDepth = invalidValue };

        // Act
        var result = config.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(expectedError);
    }

    [Theory]
    [InlineData(0, "FallbackThreshold must be greater than 0")]
    [InlineData(-1, "FallbackThreshold must be greater than 0")]
    public void Validate_WithInvalidFallbackThreshold_ShouldReturnFalse(int invalidValue, string expectedError)
    {
        // Arrange
        var config = new ChunkedHierarchyConfiguration { FallbackThreshold = invalidValue };

        // Act
        var result = config.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(expectedError);
    }

    #endregion

    #region Azure Functions Timeout Constraint Tests (RED phase)

    [Theory]
    [InlineData(0, "LevelProcessingTimeoutMinutes must be between 1 and 5")]
    [InlineData(-1, "LevelProcessingTimeoutMinutes must be between 1 and 5")]
    [InlineData(6, "LevelProcessingTimeoutMinutes must be between 1 and 5")]
    public void Validate_WithInvalidLevelProcessingTimeout_ShouldReturnFalse(int invalidValue, string expectedError)
    {
        // Arrange
        var config = new ChunkedHierarchyConfiguration { LevelProcessingTimeoutMinutes = invalidValue };

        // Act
        var result = config.Validate();

        // Assert - Azure Functions timeout limits must be respected (≤5 minutes)
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(expectedError);
    }

    [Theory]
    [InlineData(0, "BulkCreationTimeoutMinutes must be between 1 and 4")]
    [InlineData(-1, "BulkCreationTimeoutMinutes must be between 1 and 4")]
    [InlineData(5, "BulkCreationTimeoutMinutes must be between 1 and 4")]
    public void Validate_WithInvalidBulkCreationTimeout_ShouldReturnFalse(int invalidValue, string expectedError)
    {
        // Arrange
        var config = new ChunkedHierarchyConfiguration { BulkCreationTimeoutMinutes = invalidValue };

        // Act
        var result = config.Validate();

        // Assert - Bulk creation should be shorter than level processing
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(expectedError);
    }

    [Fact]
    public void Validate_WithBulkCreationTimeoutGreaterThanLevelProcessing_ShouldReturnFalse()
    {
        // Arrange
        var config = new ChunkedHierarchyConfiguration 
        { 
            LevelProcessingTimeoutMinutes = 3,
            BulkCreationTimeoutMinutes = 4 // Greater than level processing
        };

        // Act
        var result = config.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain("BulkCreationTimeoutMinutes cannot be greater than LevelProcessingTimeoutMinutes");
    }

    #endregion

    #region Bulk Creation Performance Configuration Tests (NEW - RED phase)

    [Fact]
    public void Validate_WithOptimalBulkCreationSettings_ShouldReturnTrue()
    {
        // Arrange - Configuration optimized for 5-10x performance improvement
        var config = new ChunkedHierarchyConfiguration
        {
            EnableBulkCreation = true,
            MaxBulkCreateSize = 50, // Optimal for BigCommerce API
            BatchSizePerLevel = 25, // Efficient batching
            AdaptiveBatchSizing = true // Enable dynamic optimization
        };

        // Act
        var result = config.Validate();

        // Assert - Should be valid for optimal performance
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithBulkCreationDisabled_ShouldShowWarning()
    {
        // Arrange
        var config = new ChunkedHierarchyConfiguration { EnableBulkCreation = false };

        // Act
        var result = config.Validate();

        // Assert - Should be valid but with performance warning
        result.IsValid.Should().BeTrue();
        result.ValidationWarnings.Should().Contain("EnableBulkCreation is disabled - performance will be significantly reduced");
    }

    [Fact]
    public void Validate_WithNonOptimalBatchSize_ShouldShowWarning()
    {
        // Arrange
        var config = new ChunkedHierarchyConfiguration 
        { 
            BatchSizePerLevel = 5, // Too small for optimal performance
            MaxBulkCreateSize = 50
        };

        // Act
        var result = config.Validate();

        // Assert - Should be valid but with performance warning
        result.IsValid.Should().BeTrue();
        result.ValidationWarnings.Should().Contain("BatchSizePerLevel below 20 may impact performance - consider increasing for better throughput");
    }

    #endregion

    #region Memory Safety Configuration Tests (RED phase)

    [Fact]
    public void Validate_WithMemoryMonitoringDisabled_ShouldShowWarning()
    {
        // Arrange
        var config = new ChunkedHierarchyConfiguration { EnableMemoryMonitoring = false };

        // Act
        var result = config.Validate();

        // Assert - Should be valid but with memory safety warning
        result.IsValid.Should().BeTrue();
        result.ValidationWarnings.Should().Contain("EnableMemoryMonitoring is disabled - memory usage will not be tracked");
    }

    [Theory]
    [InlineData(100000, "MaxCategoriesPerLevel exceeds recommended limit - consider chunking strategy")]
    [InlineData(50000, "MaxCategoriesPerLevel exceeds recommended limit - consider chunking strategy")]
    public void Validate_WithHighCategoryCount_ShouldShowMemoryWarning(int categoryCount, string expectedWarning)
    {
        // Arrange
        var config = new ChunkedHierarchyConfiguration { MaxCategoriesPerLevel = categoryCount };

        // Act
        var result = config.Validate();

        // Assert - Should be valid but with memory warning
        result.IsValid.Should().BeTrue();
        result.ValidationWarnings.Should().Contain(expectedWarning);
    }

    #endregion

    #region Multiple Validation Errors Test (RED phase)

    [Fact]
    public void Validate_WithMultipleInvalidValues_ShouldReturnAllErrors()
    {
        // Arrange - Multiple invalid values
        var config = new ChunkedHierarchyConfiguration
        {
            MaxCategoriesPerLevel = -1,
            BatchSizePerLevel = 0,
            MaxBulkCreateSize = 101,
            MaxHierarchyDepth = 0,
            LevelProcessingTimeoutMinutes = 6
        };

        // Act
        var result = config.Validate();

        // Assert - Should collect all validation errors
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().HaveCount(5);
        result.ValidationErrors.Should().Contain("MaxCategoriesPerLevel must be greater than 0");
        result.ValidationErrors.Should().Contain("BatchSizePerLevel must be between 1 and 100");
        result.ValidationErrors.Should().Contain("MaxBulkCreateSize must be between 1 and 100");
        result.ValidationErrors.Should().Contain("MaxHierarchyDepth must be between 1 and 20");
        result.ValidationErrors.Should().Contain("LevelProcessingTimeoutMinutes must be between 1 and 5");
    }

    #endregion

    #region SRP Validation - Configuration Responsibility Only (RED phase)

    [Fact]
    public void ChunkedHierarchyConfiguration_ShouldOnly_HandleConfigurationConcerns()
    {
        // Assert - Verify class follows Single Responsibility Principle
        var configType = typeof(ChunkedHierarchyConfiguration);
        
        // Should only have configuration properties, validation method, and standard object methods
        var methods = configType.GetMethods().Where(m => m.DeclaringType == configType && m.IsPublic);
        
        foreach (var method in methods)
        {
            var isPropertyAccessor = method.Name.StartsWith("get_") || method.Name.StartsWith("set_");
            var isValidMethod = method.Name == "Validate";
            var isStandardObjectMethod = new[] { "GetHashCode", "Equals", "ToString", "GetType" }.Contains(method.Name);
            
            (isPropertyAccessor || isValidMethod || isStandardObjectMethod).Should().BeTrue(
                $"Configuration class should only handle configuration concerns (properties, Validate method, standard object methods), but found unexpected method: {method.Name}");
        }
    }

    #endregion
}

/// <summary>
/// Validation result model for configuration validation
/// This will also need to be implemented as part of the configuration
/// </summary>
public class ConfigurationValidationResult
{
    public bool IsValid { get; set; }
    public List<string> ValidationErrors { get; set; } = new List<string>();
    public List<string> ValidationWarnings { get; set; } = new List<string>();
}